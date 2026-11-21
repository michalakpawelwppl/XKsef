namespace xKsef.Application.Contractors;

public static class ContractorIdentifierNormalizer
{
    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
