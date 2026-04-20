namespace UploadCli.Models;

/// <summary>
/// All configuration options parsed from the command line.
/// </summary>
public sealed record UploadOptions
{
    // ── BC SaaS connection ──────────────────────────────────────────────────
    /// <summary>Azure tenant ID (GUID or *.onmicrosoft.com).</summary>
    public required string Tenant { get; init; }

    /// <summary>Business Central environment name, e.g. "Production".</summary>
    public required string Environment { get; init; }

    /// <summary>Business Central company name (URL-segment form).</summary>
    public required string Company { get; init; }

    /// <summary>
    /// Optional base URL override.  Defaults to the standard SaaS root
    /// https://api.businesscentral.dynamics.com/v2.0
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.businesscentral.dynamics.com/v2.0";

    // ── Entra ID (OAuth2 client credentials) ───────────────────────────────
    /// <summary>Entra ID Application (client) ID.</summary>
    public required string ClientId { get; init; }

    /// <summary>Entra ID client secret.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// OAuth2 scope.  Defaults to the BC SaaS API scope
    /// https://api.businesscentral.dynamics.com/.default
    /// </summary>
    public string Scope { get; init; } = "https://api.businesscentral.dynamics.com/.default";

    // ── Input file ──────────────────────────────────────────────────────────
    /// <summary>Path to the input CSV or JSON file.</summary>
    public required string InputFile { get; init; }

    /// <summary>Input format: "csv" or "json".</summary>
    public string Format { get; init; } = "csv";

    // ── Payload options ─────────────────────────────────────────────────────
    /// <summary>BC table number to upsert into.</summary>
    public required int TableId { get; init; }

    /// <summary>
    /// Comma-separated list of field IDs to use as key fields for upsert.
    /// If empty the AL extension falls back to the table's primary key.
    /// </summary>
    public int[]? KeyFieldIds { get; init; }

    /// <summary>Whether to run BC table triggers on insert/modify.</summary>
    public bool RunTriggers { get; init; } = false;

    // ── Batching / resilience ───────────────────────────────────────────────
    /// <summary>Number of rows per HTTP batch. Default 500.</summary>
    public int BatchSize { get; init; } = 500;

    // ── Logging ─────────────────────────────────────────────────────────────
    /// <summary>Path to write per-row error log. Defaults to error-{timestamp}.json.</summary>
    public string? ErrorLogFile { get; init; }
}
