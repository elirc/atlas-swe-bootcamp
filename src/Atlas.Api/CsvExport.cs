using System.Globalization;
using System.Text;

namespace Atlas.Api;

public readonly record struct CsvNumber(decimal Value, string Format);

/// <summary>CSV structure and spreadsheet text protection are separate formatting steps.</summary>
public static class CsvExport
{
    public static byte[] Write(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', headers.Select(Text)).Append("\r\n");
        foreach (var row in rows) csv.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Cell(object? value) => value switch
    {
        null => "",
        decimal money => money.ToString("0.00", CultureInfo.InvariantCulture),
        CsvNumber number => number.Value.ToString(number.Format, CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        DateTimeOffset timestamp => Text(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static string Text(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        if ((!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@')
            || (value.Length > 0 && value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
