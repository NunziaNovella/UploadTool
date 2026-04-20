using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using UploadCli.Models;

namespace UploadCli.IO;

/// <summary>
/// Streams rows from a CSV file as <see cref="IList{BcField}"/> sequences.
///
/// CSV column header convention:
///   • Columns named <c>id_&lt;fieldId&gt;</c> (e.g. <c>id_2</c>, <c>id_3</c>)
///     are mapped directly to the BC field ID.
///   • Columns not matching that pattern are ignored with a warning.
///   • An empty cell value is treated as <c>null</c> (skip field).
///
/// Example header row:
///   id_2,id_3,id_4,id_50
/// </summary>
public static class CsvStreamReader
{
    private const string ColumnPrefix = "id_";

    /// <summary>
    /// Lazily yields one <see cref="IList{BcField}"/> per CSV row.
    /// The file is read line-by-line; the full file is never buffered in memory.
    /// </summary>
    public static async IAsyncEnumerable<IList<BcField>> ReadAsync(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            useAsync: true);

        using var textReader = new StreamReader(fileStream);
        using var csv = new CsvReader(textReader, config);

        // Read header
        await csv.ReadAsync();
        csv.ReadHeader();

        var headers = csv.HeaderRecord ?? [];
        var columnMapping = BuildColumnMapping(headers);

        if (columnMapping.Count == 0)
        {
            Console.Error.WriteLine(
                "Warning: no columns matching the 'id_<fieldId>' pattern were found in the CSV.");
        }

        while (await csv.ReadAsync())
        {
            var fields = new List<BcField>(columnMapping.Count);

            foreach (var (columnName, fieldId) in columnMapping)
            {
                var rawValue = csv.GetField(columnName);
                fields.Add(new BcField
                {
                    Id = fieldId,
                    Value = string.IsNullOrEmpty(rawValue) ? null : (object)rawValue,
                });
            }

            yield return fields;
        }
    }

    private static Dictionary<string, int> BuildColumnMapping(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (header.StartsWith(ColumnPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var idPart = header[ColumnPrefix.Length..];
                if (int.TryParse(idPart, out var fieldId))
                    map[header] = fieldId;
                else
                    Console.Error.WriteLine(
                        $"Warning: CSV column '{header}' has prefix '{ColumnPrefix}' but non-integer suffix – skipped.");
            }
        }

        return map;
    }
}
