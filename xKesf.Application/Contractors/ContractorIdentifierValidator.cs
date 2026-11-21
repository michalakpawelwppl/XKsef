using System.Text.RegularExpressions;

namespace xKsef.Application.Contractors;

/// <summary>
/// Normalizes optional identifiers and validates their format locally, without querying registers.
/// A null result means that no identifier was supplied; a lookup caller must require its search identifier separately.
/// </summary>
public static class ContractorIdentifierValidator
{
    public static string? NormalizeAndValidateNip(string? nip)
    {
        var value = ContractorIdentifierNormalizer.NormalizeOptional(nip);
        if (value is null)
            return null;

        var nipDigits = Regex.Replace(value, @"[ -]", "");

        if (!Regex.IsMatch(value, @"^[0-9 -]+$") || nipDigits.Length != 10)
            throw new ArgumentException("NIP musi zawierać 10 cyfr. Dozwolone separatory: spacja i myślnik.", nameof(nip));

        var checksum = CalculateRemainder(nipDigits, [6, 5, 7, 2, 3, 4, 5, 6, 7]);
        if (checksum == 10 || checksum != nipDigits[9] - '0')
            throw new ArgumentException("Nieprawidłowa suma kontrolna NIP.", nameof(nip));

        return value;
    }

    public static string? NormalizeAndValidateRegon(string? regon)
    {
        var value = ContractorIdentifierNormalizer.NormalizeOptional(regon);
        if (value is null)
            return null;

        if (value.Length is not (9 or 14) || !ContainsOnlyAsciiDigits(value))
            throw new ArgumentException("REGON musi zawierać 9 lub 14 cyfr.", nameof(regon));

        var parentChecksum = CalculateRemainder(value, [8, 9, 2, 3, 4, 5, 6, 7]) % 10;
        if (parentChecksum != value[8] - '0')
            throw new ArgumentException("Nieprawidłowa suma kontrolna REGON.", nameof(regon));

        if (value.Length == 14)
        {
            var checksum = CalculateRemainder(value, [2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8]) % 10;
            if (checksum != value[13] - '0')
                throw new ArgumentException("Nieprawidłowa suma kontrolna REGON jednostki lokalnej.", nameof(regon));
        }

        return value;
    }

    public static string? NormalizeAndValidateKrsNumber(string? krsNumber)
    {
        var value = ContractorIdentifierNormalizer.NormalizeOptional(krsNumber);
        if (value is not null && (value.Length != 10 || !ContainsOnlyAsciiDigits(value)))
            throw new ArgumentException("Numer KRS musi zawierać 10 cyfr.", nameof(krsNumber));

        return value;
    }

    public static string? NormalizeAndValidateEuVatNumber(string? euVatNumber, bool isEuVatRegistered)
    {
        var value = ContractorIdentifierNormalizer.NormalizeOptional(euVatNumber);
        if (value?.Length > 32)
            throw new ArgumentException("Numer VAT UE nie może przekraczać 32 znaków.", nameof(euVatNumber));

        if (isEuVatRegistered && value is null)
            throw new ArgumentException("Numer VAT UE jest wymagany przy rejestracji do VAT UE.", nameof(euVatNumber));

        if (!isEuVatRegistered && value is not null)
            throw new ArgumentException("Numer VAT UE musi być pusty, gdy kontrahent nie jest zarejestrowany do VAT UE.", nameof(euVatNumber));

        return value;
    }

    public static string? NormalizeAndValidateOtherTaxNumber(string? otherTaxNumber)
    {
        var value = ContractorIdentifierNormalizer.NormalizeOptional(otherTaxNumber);
        if (value?.Length > 100)
            throw new ArgumentException("Inny numer podatkowy nie może przekraczać 100 znaków.", nameof(otherTaxNumber));

        return value;
    }

    private static bool ContainsOnlyAsciiDigits(string value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static int CalculateRemainder(string value, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
            sum += (value[i] - '0') * weights[i];

        return sum % 11;
    }
}
