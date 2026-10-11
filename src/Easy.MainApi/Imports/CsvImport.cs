using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Easy.Data;

namespace Easy.MainApi.Imports;

// S02: the real IHK layout is unknown, so everything is configuration ("Import" section). Defaults follow the
// hand-made sample (docs/samples/antragimport).
public record ImportOptions
{
    public string Delimiter { get; init; } = ";";
    public string Encoding { get; init; } = "utf-8"; // any .NET or Windows code page name
    public string NameColumn { get; init; } = "Prüfungsteilnehmer/-in";
    public string IdColumn { get; init; } = "Prüfl.-Nr.";
    public string TitleColumn { get; init; } = "Projektbezeichnung";
    public int MaxRows { get; init; } = 500;
    public int MaxBytes { get; init; } = 1024 * 1024;
}

public record ImportRow(int Row, string Name, int CsvId, string ProjectTitle);

public static class CsvImport
{
    static CsvImport() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // windows-1252 and friends

    public record Result(List<ImportRow> Rows, List<ImportError> Errors);

    // All-or-nothing: the caller imports Rows only when Errors is empty. Errors never quote field values (personal data).
    public static Result Parse(byte[] csv, ImportOptions options)
    {
        if (csv.Length > options.MaxBytes)
            return new([], [new(0, "", $"File larger than {options.MaxBytes} bytes.")]);
        try
        {
            return ParseRows(csv, options);
        }
        catch (Exception e) when (e is CsvHelperException or DecoderFallbackException or ArgumentException) // bad quoting, bad bytes, unknown encoding
        {
            return new([], [new(e is CsvHelperException { Context.Parser: { } p } ? p.Row : 0, "", "File cannot be read.")]);
        }
    }

    static Result ParseRows(byte[] csv, ImportOptions options)
    {
        List<ImportRow> rows = [];
        List<ImportError> errors = [];
        // A UTF-8 BOM is accepted; invalid bytes throw instead of silently becoming U+FFFD in stored names.
        int bom = csv.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? 3 : 0;
        var encoding = System.Text.Encoding.GetEncoding(bom > 0 ? "utf-8" : options.Encoding, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        using var parser = new CsvReader(new StringReader(encoding.GetString(csv, bom, csv.Length - bom)), new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = options.Delimiter,
            PrepareHeaderForMatch = a => Normalize(a.Header),
            MissingFieldFound = null,
        });
        string[] columns = [options.NameColumn, options.IdColumn, options.TitleColumn];

        if (!parser.Read())
            return new(rows, [new(0, "", "File is empty.")]);
        parser.ReadHeader();
        var header = parser.HeaderRecord!.Select(Normalize).ToHashSet();
        errors.AddRange(columns.Where(c => !header.Contains(Normalize(c))).Select(c => new ImportError(1, c, "Column missing.")));
        if (errors.Count > 0)
            return new(rows, errors);

        Dictionary<int, int> firstRowOfId = [];
        for (int count = 1; parser.Read(); count++)
        {
            if (count > options.MaxRows)
                return new([], [new(0, "", $"More than {options.MaxRows} rows.")]);
            int row = parser.Parser.Row;

            var values = columns.Select(c => parser.GetField(c)?.Trim() ?? "").ToArray();
            for (int i = 0; i < columns.Length; i++)
                if (values[i].Length == 0)
                    errors.Add(new(row, columns[i], "Required."));
            if (values.Any(v => v.Length == 0))
                continue;

            if (!int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                errors.Add(new(row, options.IdColumn, "Not an integer."));
                continue;
            }
            if (!firstRowOfId.TryAdd(id, row))
            {
                errors.Add(new(row, options.IdColumn, $"Duplicate of row {firstRowOfId[id]}."));
                continue;
            }
            rows.Add(new(row, values[0], id, values[2]));
        }
        if (rows.Count == 0 && errors.Count == 0)
            errors.Add(new(0, "", "No data rows."));
        return new(errors.Count == 0 ? rows : [], errors);
    }

    static string Normalize(string header) => header.Trim().ToLowerInvariant();
}
