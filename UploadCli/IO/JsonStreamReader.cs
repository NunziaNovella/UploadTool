using System.Runtime.CompilerServices;
using System.Text.Json;
using UploadCli.Models;

namespace UploadCli.IO;

/// <summary>
/// Streams rows from a JSON file as <see cref="IList{BcField}"/> sequences.
///
/// Expected JSON format – a top-level array where each element represents one row:
/// <code>
/// [
///   { "fields": [ { "id": 2, "value": "C00010" }, { "id": 3, "value": "Test" } ] },
///   { "fields": [ { "id": 2, "value": "C00011" }, { "id": 3, "value": null  } ] }
/// ]
/// </code>
///
/// A <c>null</c> value instructs the AL extension to skip that field.
/// The array is parsed using <see cref="JsonSerializer.DeserializeAsyncEnumerable{T}"/>
/// so the file is never fully buffered in memory.
/// </summary>
public static class JsonStreamReader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Lazily yields one <see cref="IList{BcField}"/> per JSON array element.
    /// </summary>
    public static async IAsyncEnumerable<IList<BcField>> ReadAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            useAsync: true);

        await foreach (var row in JsonSerializer
            .DeserializeAsyncEnumerable<JsonRowInput>(fileStream, JsonOpts, ct)
            .ConfigureAwait(false))
        {
            if (row is null) continue;

            var fields = new List<BcField>(row.Fields.Count);
            foreach (var f in row.Fields)
            {
                fields.Add(new BcField
                {
                    Id = f.Id,
                    Value = ConvertJsonElement(f.RawValue),
                });
            }

            yield return fields;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the most appropriate .NET type.
    /// Returns <c>null</c> for JSON null, which instructs AL to skip the field.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement? element)
    {
        if (element is null) return null;

        return element.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => (object)true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.Value.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.Value.GetDouble(),
            _ => element.Value.GetRawText().Trim('"'),
        };
    }

    // ── Local DTO used only for deserialisation ──────────────────────────────

    private sealed class JsonRowInput
    {
        public List<JsonFieldInput> Fields { get; set; } = [];
    }

    private sealed class JsonFieldInput
    {
        public int Id { get; set; }

        // Keep as raw JsonElement so we can inspect the value kind
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public JsonElement? RawValue { get; set; }
    }
}
