using System.Text;
using Easy.MainApi.Imports;

namespace Easy.MainApi.Tests;

// Spec 6 (issue 21), S02 parsing and all-or-nothing validation.
public class CsvImportTests
{
    const string Header = "Prüfungsteilnehmer/-in;Prüfl.-Nr.;Ausbildungsstätte;Status;Projektbezeichnung";

    static CsvImport.Result Parse(string csv, ImportOptions? options = null) =>
        CsvImport.Parse(Encoding.UTF8.GetBytes(csv), options ?? new());

    [Fact]
    public void Valid_rows_are_parsed_in_file_order()
    {
        var result = Parse($"{Header}\nErika Muster;4711;Firma;egal;Webshop\nMax Mustermann; 0042 ;Firma / Backhaus IT;egal;CRM\n");

        Assert.Empty(result.Errors);
        Assert.Equal([new(2, "Erika Muster", 4711, "Webshop"), new(3, "Max Mustermann", 42, "CRM")], result.Rows);
    }

    [Fact]
    public void Bom_and_case_insensitive_headers_are_accepted()
    {
        byte[] csv = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("PRÜFUNGSTEILNEHMER/-IN;prüfl.-nr.;projektbezeichnung\nErika;1;Shop\n")];

        var result = CsvImport.Parse(csv, new());

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Missing_required_column_is_an_error()
    {
        var result = Parse("Prüfungsteilnehmer/-in;Projektbezeichnung\nErika;Shop\n");

        Assert.Equal([new(1, "Prüfl.-Nr.", "Column missing.")], result.Errors);
    }

    [Fact]
    public void Empty_field_and_non_integer_id_are_row_errors()
    {
        var result = Parse($"{Header}\n;4711;F;S;Shop\nMax;12a;F;S;CRM\nEva;7;F;S;\n");

        Assert.Equal(
        [
            new(2, "Prüfungsteilnehmer/-in", "Required."),
            new(3, "Prüfl.-Nr.", "Not an integer."),
            new(4, "Projektbezeichnung", "Required."),
        ], result.Errors);
    }

    [Fact]
    public void Duplicate_id_is_a_row_error()
    {
        var result = Parse($"{Header}\nErika;4711;F;S;Shop\nMax;04711;F;S;CRM\n");

        Assert.Equal([new(3, "Prüfl.-Nr.", "Duplicate of row 2.")], result.Errors);
    }

    [Fact]
    public void Too_many_rows_and_too_many_bytes_are_errors()
    {
        var options = new ImportOptions { MaxRows = 2, MaxBytes = 100 };

        Assert.Equal([new(0, "", "More than 2 rows.")], Parse($"{Header}\nA;1;F;S;T\nB;2;F;S;T\nC;3;F;S;T\n", options with { MaxBytes = 1000 }).Errors);
        Assert.Equal([new(0, "", "File larger than 100 bytes.")], Parse($"{Header}\n{new string('x', 100)};1;F;S;T\n", options).Errors);
    }

    [Fact]
    public void Unreadable_or_empty_files_are_errors()
    {
        Assert.Equal([new(0, "", "No data rows.")], Parse($"{Header}\n").Errors);
        Assert.Equal([new(0, "", "File cannot be read.")], CsvImport.Parse([.. Encoding.UTF8.GetBytes($"{Header}\nEr"), 0xFF, .. Encoding.UTF8.GetBytes(";1;F;S;T\n")], new()).Errors);
        Assert.Equal("File cannot be read.", Assert.Single(Parse($"{Header}\nEri\"ka;1;F;S;T\n").Errors).Message);
    }

    [Fact]
    public void Windows_1252_is_supported()
    {
        byte[] csv = CodePagesEncoding().GetBytes("name;id;title\nJürgen;1;Übung\n");

        var result = CsvImport.Parse(csv, new() { Encoding = "windows-1252", NameColumn = "name", IdColumn = "id", TitleColumn = "title" });

        Assert.Equal([new(2, "Jürgen", 1, "Übung")], result.Rows);
    }

    static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("windows-1252");
    }

    [Fact]
    public void Delimiter_and_column_names_are_configurable()
    {
        var options = new ImportOptions { Delimiter = ",", NameColumn = "name", IdColumn = "id", TitleColumn = "title" };

        var result = Parse("id,name,title\n5,Erika,Shop\n", options);

        Assert.Equal([new(2, "Erika", 5, "Shop")], result.Rows);
    }
}
