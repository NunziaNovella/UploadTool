using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.Retry;
using UploadCli.Models;

namespace UploadCli.Services;

/// <summary>
/// Posts batched row payloads to the BC BCUpload API endpoint.
///
/// Features:
/// • Builds the OData POST URL from the supplied options.
/// • Acquires and refreshes Bearer tokens via <see cref="BcAuthService"/>.
/// • Applies a Polly retry policy: exponential back-off on HTTP 429 and
///   transient 5xx responses (up to 4 attempts).
/// • Streams responses and reports per-batch progress to the console.
/// • Collects per-row errors and appends them to a JSON log file.
/// </summary>
public sealed class BcUploaderService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly UploadOptions _opts;
    private readonly BcAuthService _auth;
    private readonly HttpClient _http;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly string _apiUrl;
    private readonly string _errorLogPath;

    // Running totals
    private int _totalAccepted;
    private int _totalSucceeded;
    private int _totalFailed;
    private int _batchNumber;

    // A single long-lived HttpClient shared across all BcUploaderService instances.
    // For a CLI tool the process lifetime is short, but a static instance ensures
    // the underlying socket pool is shared rather than exhausted by repeated calls.
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public BcUploaderService(UploadOptions opts, BcAuthService auth)
    {
        _opts = opts;
        _auth = auth;
        _http = SharedHttpClient;
        _retryPolicy = BuildRetryPolicy();

        // Example URL:
        // https://api.businesscentral.dynamics.com/v2.0/{tenant}/{env}/api/nunziaNovella/upload/v1.0/companies({company})/uploadRequests
        _apiUrl = $"{opts.BaseUrl.TrimEnd('/')}/{opts.Tenant}/{opts.Environment}" +
                  $"/api/nunziaNovella/upload/v1.0/companies('{opts.Company}')/uploadRequests";

        _errorLogPath = opts.ErrorLogFile
            ?? $"error-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads rows from <paramref name="rowSource"/>, batches them, and posts
    /// each batch to BC.  Emits progress to stdout; writes errors to the log
    /// file.  Returns the aggregate <see cref="UploadResult"/>.
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        IAsyncEnumerable<IList<BcField>> rowSource,
        CancellationToken ct = default)
    {
        var batch = new List<BcRow>(_opts.BatchSize);

        await foreach (var fields in rowSource.WithCancellation(ct))
        {
            batch.Add(new BcRow { Fields = [.. fields] });

            if (batch.Count >= _opts.BatchSize)
            {
                await PostBatchAsync(batch, ct);
                batch.Clear();
            }
        }

        // Flush remaining rows
        if (batch.Count > 0)
            await PostBatchAsync(batch, ct);

        var summary = new UploadResult
        {
            Accepted = _totalAccepted,
            Succeeded = _totalSucceeded,
            Failed = _totalFailed,
        };

        Console.WriteLine();
        Console.WriteLine(
            $"Done. Accepted={summary.Accepted}  Succeeded={summary.Succeeded}  Failed={summary.Failed}");
        if (_totalFailed > 0)
            Console.WriteLine($"Row-level errors saved to: {_errorLogPath}");

        return summary;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PostBatchAsync(IList<BcRow> rows, CancellationToken ct)
    {
        _batchNumber++;

        var payload = new UploadPayload
        {
            TableId = _opts.TableId,
            RunTriggers = _opts.RunTriggers,
            Mode = "upsert",
            KeyFieldIds = _opts.KeyFieldIds,
            Rows = [.. rows],
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOpts);
        var body = new UploadRequestBody { PayloadText = payloadJson };
        var bodyJson = JsonSerializer.Serialize(body, JsonOpts);

        Console.Write($"  Batch {_batchNumber} ({rows.Count} rows) … ");

        // Refresh token for every batch (MSAL caches; only hits AAD when near expiry)
        var token = await _auth.GetAccessTokenAsync(ct);

        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            return await _http.SendAsync(request, ct);
        });

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"HTTP {(int)response.StatusCode} – {detail}");
                await AppendErrorsAsync(
                    _batchNumber,
                    [new RowError { RowIndex = -1, FieldId = -1, Message = $"HTTP {(int)response.StatusCode}: {detail}" }],
                    ct);
                _totalFailed += rows.Count;
                return;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var entity = JsonSerializer.Deserialize<UploadRequestResponse>(responseJson, JsonOpts);

            UploadResult? result = null;
            if (entity?.ResponseText is { Length: > 0 } rt)
                result = JsonSerializer.Deserialize<UploadResult>(rt, JsonOpts);

            if (result is not null)
            {
                _totalAccepted += result.Accepted;
                _totalSucceeded += result.Succeeded;
                _totalFailed += result.Failed;

                Console.WriteLine($"accepted={result.Accepted}  ok={result.Succeeded}  failed={result.Failed}");

                if (result.Errors.Count > 0)
                    await AppendErrorsAsync(_batchNumber, result.Errors, ct);
            }
            else
            {
                _totalAccepted += rows.Count;
                _totalSucceeded += rows.Count;
                Console.WriteLine("ok");
            }
        }
    }

    private async Task AppendErrorsAsync(int batchNum, IList<RowError> errors, CancellationToken ct)
    {
        await using var writer = new StreamWriter(_errorLogPath, append: true, Encoding.UTF8);
        foreach (var err in errors)
        {
            var entry = new
            {
                batch = batchNum,
                err.RowIndex,
                err.FieldId,
                err.Message,
                timestamp = DateTime.UtcNow,
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry));
        }
    }

    private static AsyncRetryPolicy<HttpResponseMessage> BuildRetryPolicy()
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r =>
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(
                retryCount: 4,
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    // Honour Retry-After header on 429; otherwise exponential back-off
                    if (outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                        return delta;
                    return TimeSpan.FromSeconds(Math.Pow(2, attempt));
                },
                onRetryAsync: (outcome, delay, attempt, _) =>
                {
                    Console.WriteLine(
                        $"  [retry {attempt}] status={(int?)outcome.Result?.StatusCode} – waiting {delay.TotalSeconds:F1}s …");
                    return Task.CompletedTask;
                });
    }

    // HttpClient is static (shared) and must not be disposed by this instance.
    public void Dispose() { }
}
