using System.Globalization;
using System.Text;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Domain.InvoiceNumberSeries;

/// <summary>Pure formatting and exact intersection of format languages. Never allocates or reserves a number.</summary>
public static class InvoiceNumberFormat
{
    private const string Number = "{number}";
    private const string Year = "{year}";
    private const string Prefix = "{prefix}";

    public static void Validate(NumberSeriesConfiguration configuration)
    {
        var issues = new List<InvoiceValidationIssue>();
        void Check(bool valid, string path, string message) { if (!valid) issues.Add(new(path, "InvalidConfiguration", message)); }
        Check(!string.IsNullOrWhiteSpace(configuration.Name) && configuration.Name.Length <= 100 && !configuration.Name.Any(char.IsControl), "name", "Nazwa musi zawierać 1–100 znaków bez znaków sterujących.");
        Check(!string.IsNullOrWhiteSpace(configuration.Prefix) && configuration.Prefix.Length <= 50 && configuration.Prefix.All(c => c >= '!' && c <= '~' && c is not '{' and not '}'), "prefix", "Prefiks musi zawierać 1–50 widocznych znaków ASCII bez nawiasów klamrowych.");
        Check(configuration.MinimumDigits is >= 1 and <= 19, "minimumDigits", "Minimalna liczba cyfr musi wynosić od 1 do 19.");
        Check(configuration.StartingNumber > 0, "startingNumber", "Numer początkowy musi być dodatni.");
        Check(Enum.IsDefined(configuration.ResetMode), "resetMode", "Nieprawidłowy sposób resetowania numeracji.");
        var format = configuration.Format ?? "";
        var remainder = format.Replace(Prefix, "", StringComparison.Ordinal).Replace(Number, "", StringComparison.Ordinal).Replace(Year, "", StringComparison.Ordinal);
        Check(format.Length is > 0 and <= 150 && remainder.All(c => c >= '!' && c <= '~' && c is not '{' and not '}'), "format", "Format może zawierać widoczne znaki ASCII i znaczniki {prefix}, {number}, {year}.");
        Check(Count(format, Number) == 1 && Count(format, Prefix) <= 1 && Count(format, Year) <= 1, "format", "Format wymaga jednego {number}; {prefix} i {year} mogą wystąpić najwyżej raz.");
        Check(configuration.ResetMode != NumberSeriesResetMode.Yearly || Count(format, Year) == 1, "format", "Reset roczny wymaga znacznika {year}.");
        // StoredInvoice.InvoiceNumber currently has a 100-character limit. Padding is a minimum, never truncation.
        Check(format.Replace(Prefix, configuration.Prefix ?? "", StringComparison.Ordinal).Replace(Number, new string('9', 19), StringComparison.Ordinal).Replace(Year, "9999", StringComparison.Ordinal).Length <= 100, "format", "Pełny numer nie może przekraczać istniejącego limitu 100 znaków.");
        if (issues.Count != 0) throw new InvoiceNumberSeriesValidationException(issues);
    }

    public static string Render(NumberSeriesConfiguration configuration, long number, DateOnly issueDate)
    {
        Validate(configuration);
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        return configuration.Format.Replace(Prefix, configuration.Prefix, StringComparison.Ordinal)
            .Replace(Number, number.ToString("D" + configuration.MinimumDigits, CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(Year, issueDate.Year.ToString("D4", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public static string? FindCollision(NumberSeriesConfiguration left, NumberSeriesConfiguration right)
    {
        Validate(left); Validate(right);
        var rightPatterns = Patterns(right);
        foreach (var a in Patterns(left))
        foreach (var b in rightPatterns)
        {
            if (a.Length != b.Length) continue;
            var witness = new StringBuilder(a.Length);
            for (var i = 0; i < a.Length; i++)
            {
                var min = (char)Math.Max(a[i].Min, b[i].Min); var max = (char)Math.Min(a[i].Max, b[i].Max);
                if (min > max) break;
                witness.Append(min);
            }
            if (witness.Length == a.Length) return witness.ToString();
        }
        return null;
    }

    private static int Count(string text, string token) => (text.Length - text.Replace(token, "", StringComparison.Ordinal).Length) / token.Length;
    private readonly record struct CharacterRange(char Min, char Max);

    private static IReadOnlyList<CharacterRange[]> Patterns(NumberSeriesConfiguration configuration)
    {
        // Case-insensitive comparison matches the existing SQL Server number uniqueness semantics.
        var text = configuration.Format.Replace(Prefix, configuration.Prefix, StringComparison.Ordinal).ToUpperInvariant();
        var patterns = new List<CharacterRange[]> { Array.Empty<CharacterRange>() };
        for (var position = 0; position < text.Length;)
        {
            IReadOnlyList<CharacterRange[]> choices;
            if (text.AsSpan(position).StartsWith(Number.ToUpperInvariant(), StringComparison.Ordinal)) { choices = Numbers(configuration.MinimumDigits); position += Number.Length; }
            else if (text.AsSpan(position).StartsWith(Year.ToUpperInvariant(), StringComparison.Ordinal)) { choices = Ranges("0001", "9999"); position += Year.Length; }
            else { choices = [new[] { new CharacterRange(text[position], text[position]) }]; position++; }
            patterns = patterns.SelectMany(p => choices.Select(c => p.Concat(c).ToArray())).ToList();
        }
        return patterns;
    }

    private static IReadOnlyList<CharacterRange[]> Numbers(int minimumDigits)
    {
        var result = new List<CharacterRange[]>();
        for (var length = 1; length <= 19; length++)
        {
            var lower = "1" + new string('0', length - 1);
            var upper = length == 19 ? long.MaxValue.ToString(CultureInfo.InvariantCulture) : new string('9', length);
            var padding = Enumerable.Repeat(new CharacterRange('0', '0'), Math.Max(0, minimumDigits - length));
            result.AddRange(Ranges(lower, upper).Select(p => padding.Concat(p).ToArray()));
        }
        return result;
    }

    // Partition a bounded decimal interval into character-range patterns without enumerating numbers.
    private static IReadOnlyList<CharacterRange[]> Ranges(string lower, string upper)
    {
        var results = new List<CharacterRange[]>();
        void Visit(int position, bool lowTight, bool highTight, List<CharacterRange> path)
        {
            if (position == lower.Length) { results.Add(path.ToArray()); return; }
            var min = lowTight ? lower[position] : '0'; var max = highTight ? upper[position] : '9';
            var lowRestricted = lowTight && lower.AsSpan(position + 1).IndexOfAnyExcept('0') >= 0;
            var highRestricted = highTight && upper.AsSpan(position + 1).IndexOfAnyExcept('9') >= 0;
            for (var first = min; first <= max;)
            {
                var low = lowRestricted && first == lower[position]; var high = highRestricted && first == upper[position];
                var last = first;
                while (last < max && (lowRestricted && last + 1 == lower[position]) == low && (highRestricted && last + 1 == upper[position]) == high) last++;
                path.Add(new(first, last)); Visit(position + 1, low, high, path); path.RemoveAt(path.Count - 1);
                first = (char)(last + 1);
            }
        }
        Visit(0, true, true, []);
        return results;
    }
}
