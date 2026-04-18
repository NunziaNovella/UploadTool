using System.CommandLine;
using UploadCli.IO;
using UploadCli.Models;
using UploadCli.Services;

namespace UploadCli.Commands;

/// <summary>
/// Defines and wires the <c>upload</c> sub-command with all supported options.
/// </summary>
public static class UploadCommand
{
    public static Command Build()
    {
        var cmd = new Command("upload", "Upload a CSV or JSON file to a BC SaaS table.");

        // ── BC SaaS connection ────────────────────────────────────────────────
        var tenantOpt = new Option<string>(
            "--tenant", "Azure AD tenant ID (GUID or *.onmicrosoft.com)") { IsRequired = true };

        var environmentOpt = new Option<string>(
            "--environment", "BC environment name, e.g. 'Production'") { IsRequired = true };

        var companyOpt = new Option<string>(
            "--company", "BC company name as it appears in the OData URL") { IsRequired = true };

        var baseUrlOpt = new Option<string>(
            "--base-url",
            () => "https://api.businesscentral.dynamics.com/v2.0",
            "BC SaaS base URL (override for on-prem or sandbox)");

        // ── Entra ID ──────────────────────────────────────────────────────────
        var clientIdOpt = new Option<string>(
            "--client-id", "Entra ID Application (client) ID") { IsRequired = true };

        var clientSecretOpt = new Option<string>(
            "--client-secret", "Entra ID client secret") { IsRequired = true };

        var scopeOpt = new Option<string>(
            "--scope",
            () => "https://api.businesscentral.dynamics.com/.default",
            "OAuth2 scope");

        // ── Input ─────────────────────────────────────────────────────────────
        var inputFileOpt = new Option<FileInfo>(
            "--input", "Path to the input CSV or JSON file") { IsRequired = true };
        inputFileOpt.ExistingOnly();

        var formatOpt = new Option<string>(
            "--format",
            () => "csv",
            "Input format: 'csv' or 'json'");
        formatOpt.FromAmong("csv", "json");

        // ── Payload ───────────────────────────────────────────────────────────
        var tableIdOpt = new Option<int>(
            "--table-id", "BC table number to upsert into") { IsRequired = true };

        var keyFieldIdsOpt = new Option<int[]?>(
            "--key-field-ids",
            "Comma-separated field IDs to use as upsert key (omit to use PK)");
        keyFieldIdsOpt.AllowMultipleArgumentsPerToken = true;

        var runTriggersOpt = new Option<bool>(
            "--run-triggers",
            () => false,
            "Run BC table triggers on insert/modify");

        // ── Batching ──────────────────────────────────────────────────────────
        var batchSizeOpt = new Option<int>(
            "--batch-size",
            () => 500,
            "Number of rows per HTTP batch");

        // ── Logging ───────────────────────────────────────────────────────────
        var errorLogOpt = new Option<string?>(
            "--error-log",
            "Path to the JSON file where row-level errors are written");

        cmd.AddOption(tenantOpt);
        cmd.AddOption(environmentOpt);
        cmd.AddOption(companyOpt);
        cmd.AddOption(baseUrlOpt);
        cmd.AddOption(clientIdOpt);
        cmd.AddOption(clientSecretOpt);
        cmd.AddOption(scopeOpt);
        cmd.AddOption(inputFileOpt);
        cmd.AddOption(formatOpt);
        cmd.AddOption(tableIdOpt);
        cmd.AddOption(keyFieldIdsOpt);
        cmd.AddOption(runTriggersOpt);
        cmd.AddOption(batchSizeOpt);
        cmd.AddOption(errorLogOpt);

        cmd.SetHandler(async ctx =>
        {
            var opts = new UploadOptions
            {
                Tenant        = ctx.ParseResult.GetValueForOption(tenantOpt)!,
                Environment   = ctx.ParseResult.GetValueForOption(environmentOpt)!,
                Company       = ctx.ParseResult.GetValueForOption(companyOpt)!,
                BaseUrl       = ctx.ParseResult.GetValueForOption(baseUrlOpt)!,
                ClientId      = ctx.ParseResult.GetValueForOption(clientIdOpt)!,
                ClientSecret  = ctx.ParseResult.GetValueForOption(clientSecretOpt)!,
                Scope         = ctx.ParseResult.GetValueForOption(scopeOpt)!,
                InputFile     = ctx.ParseResult.GetValueForOption(inputFileOpt)!.FullName,
                Format        = ctx.ParseResult.GetValueForOption(formatOpt)!,
                TableId       = ctx.ParseResult.GetValueForOption(tableIdOpt),
                KeyFieldIds   = ctx.ParseResult.GetValueForOption(keyFieldIdsOpt),
                RunTriggers   = ctx.ParseResult.GetValueForOption(runTriggersOpt),
                BatchSize     = ctx.ParseResult.GetValueForOption(batchSizeOpt),
                ErrorLogFile  = ctx.ParseResult.GetValueForOption(errorLogOpt),
            };

            await RunAsync(opts, ctx.GetCancellationToken());
        });

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task RunAsync(UploadOptions opts, CancellationToken ct)
    {
        Console.WriteLine($"Target : {opts.BaseUrl}/{opts.Tenant}/{opts.Environment}");
        Console.WriteLine($"Company: {opts.Company}  Table: {opts.TableId}");
        Console.WriteLine($"Input  : {opts.InputFile}  ({opts.Format.ToUpperInvariant()})");
        Console.WriteLine($"Batch  : {opts.BatchSize} rows");
        Console.WriteLine();

        var auth = new BcAuthService(opts.Tenant, opts.ClientId, opts.ClientSecret, opts.Scope);

        using var uploader = new BcUploaderService(opts, auth);

        IAsyncEnumerable<IList<Models.BcField>> rowSource = opts.Format.ToLowerInvariant() switch
        {
            "json" => JsonStreamReader.ReadAsync(opts.InputFile, ct),
            _      => CsvStreamReader.ReadAsync(opts.InputFile),
        };

        await uploader.UploadAsync(rowSource, ct);
    }
}
