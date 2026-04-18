using System.CommandLine;
using UploadCli.Commands;

/// <summary>
/// Entry point for the UploadCli tool.
/// Builds the System.CommandLine root command, adds all sub-commands, and invokes.
/// </summary>
var rootCommand = new RootCommand(
    "BC Upload Tool – stream CSV/JSON files to a Business Central SaaS environment " +
    "via the BCUpload AL extension API endpoint.");

rootCommand.AddCommand(UploadCommand.Build());

return await rootCommand.InvokeAsync(args);
