using System.Text.Json.Serialization;

namespace UploadCli.Models;

// ── Outbound payload ──────────────────────────────────────────────────────────

/// <summary>
/// The inner payload that is serialised to JSON, then embedded as
/// <c>payloadText</c> in the OData POST body sent to the BC API page.
/// </summary>
public sealed class UploadPayload
{
    [JsonPropertyName("tableId")]
    public int TableId { get; set; }

    [JsonPropertyName("runTriggers")]
    public bool RunTriggers { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "upsert";

    [JsonPropertyName("keyFieldIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[]? KeyFieldIds { get; set; }

    [JsonPropertyName("rows")]
    public List<BcRow> Rows { get; set; } = [];
}

/// <summary>One row of field values to upsert.</summary>
public sealed class BcRow
{
    [JsonPropertyName("fields")]
    public List<BcField> Fields { get; set; } = [];
}

/// <summary>A single field identifier + value pair.</summary>
public sealed class BcField
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// The field value.  <c>null</c> instructs the AL extension to skip the field.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

// ── OData wrapper for the BC API page POST ────────────────────────────────────

/// <summary>
/// Wraps the serialised <see cref="UploadPayload"/> JSON string inside the
/// OData entity body expected by the BC Upload API page.
/// </summary>
public sealed class UploadRequestBody
{
    [JsonPropertyName("payloadText")]
    public string PayloadText { get; set; } = string.Empty;
}

// ── Inbound response ─────────────────────────────────────────────────────────

/// <summary>The OData entity returned by the BC API page after insertion.</summary>
public sealed class UploadRequestResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("responseText")]
    public string? ResponseText { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>The structured result JSON nested inside <c>responseText</c>.</summary>
public sealed class UploadResult
{
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    [JsonPropertyName("succeeded")]
    public int Succeeded { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("errors")]
    public List<RowError> Errors { get; set; } = [];
}

/// <summary>Per-row error returned by the AL upsert engine.</summary>
public sealed class RowError
{
    [JsonPropertyName("rowIndex")]
    public int RowIndex { get; set; }

    [JsonPropertyName("fieldId")]
    public int FieldId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
