using System.Text;

namespace Atlas.Domain.Services;

public record NormalizedWorker(string FullName, string Email, string CountryCode, DateOnly? DateOfBirth);
public static class WorkerInput
{
    public static (NormalizedWorker Value, Dictionary<string, string[]> Errors) Normalize(string? name, string? email, string? country, DateOnly? birth, DateOnly today)
    {
        var value = new NormalizedWorker(name?.Trim() ?? "", email?.Trim().ToLowerInvariant() ?? "", country?.Trim().ToUpperInvariant() ?? "", birth);
        var errors = new Dictionary<string, string[]>();
        if (value.FullName.Length is < 1 or > 200) errors["fullName"] = ["Use 1–200 characters."];
        if (value.Email.Length is < 3 or > 320 || !value.Email.Contains('@')) errors["email"] = ["A valid email of at most 320 characters is required."];
        // Country creation already permits configured two-character identifiers.
        // Preserve that contract here; existence is checked by the caller.
        if (value.CountryCode.Length != 2)
            errors["countryCode"] = ["Use a configured two-character country code."];
        if (birth > today) errors["dateOfBirth"] = ["DateOfBirth cannot be in the future."];
        return (value, errors);
    }
}

/// <summary>A bounded CSV grammar: quoted separators, escaped quotes and embedded newlines.</summary>
public static class WorkerCsv
{
    public static List<string[]> Parse(string csv)
    {
        if (Encoding.UTF8.GetByteCount(csv) > 1024 * 1024) throw new DomainException("CSV exceeds 1 MiB.");
        List<string[]> rows = []; List<string> fields = []; var field = new StringBuilder();
        var quoted = false; var closed = false; var rowStarted = false;
        void Field() { fields.Add(field.ToString()); field.Clear(); closed = false; if (fields.Count > 4) throw new DomainException("CSV rows must contain four columns."); }
        void Row() { Field(); rows.Add(fields.ToArray()); fields.Clear(); rowStarted = false; if (rows.Count > 101) throw new DomainException("CSV supports at most 100 data rows."); }
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(c);
                continue;
            }
            if (c == ',') { Field(); rowStarted = true; }
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; Row(); }
            else if (c == '"' && field.Length == 0 && !closed) { quoted = true; rowStarted = true; }
            else { if (closed || c == '"') throw new DomainException("Malformed CSV quoting."); field.Append(c); rowStarted = true; }
        }
        if (quoted) throw new DomainException("CSV contains an unclosed quoted field.");
        if (rowStarted || fields.Count > 0 || field.Length > 0 || closed) Row();
        if (rows.Count < 2 || !rows[0].SequenceEqual(new[] { "FullName", "Email", "CountryCode", "DateOfBirth" }))
            throw new DomainException("Use header FullName,Email,CountryCode,DateOfBirth and 1–100 data rows.");
        if (rows.Any(x => x.Length != 4)) throw new DomainException("CSV rows must contain four columns.");
        return rows.Skip(1).ToList();
    }
}
