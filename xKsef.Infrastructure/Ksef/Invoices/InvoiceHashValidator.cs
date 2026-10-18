using System.Security.Cryptography;

namespace xKsef.Infrastructure.Invoices;

internal static class InvoiceHashValidator
{
    public static bool IsValid(
        byte[] xmlBytes,
        string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        byte[] actualHash = SHA256.HashData(xmlBytes);

        byte[] expectedHashBytes =
            Convert.FromBase64String(expectedHash);

        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHashBytes);
    }
}