using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace xKsef.CredentialService.Services;

/// <summary>Versioned plaintext package, owned and cleared by the session store.</summary>
internal sealed class SessionTokenPackage : IDisposable
{
    public byte[] AccessToken { get; }
    public byte[] RefreshToken { get; }
    public DateTimeOffset AccessTokenValidUntil { get; }
    public DateTimeOffset RefreshTokenValidUntil { get; }

    public SessionTokenPackage(byte[] accessToken, DateTimeOffset accessTokenValidUntil, byte[] refreshToken, DateTimeOffset refreshTokenValidUntil)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessTokenValidUntil = accessTokenValidUntil;
        RefreshTokenValidUntil = refreshTokenValidUntil;
    }

    public static SessionTokenPackage Create(string accessToken, DateTimeOffset accessValidUntil, string refreshToken, DateTimeOffset refreshValidUntil) =>
        new(Encoding.UTF8.GetBytes(accessToken), accessValidUntil, Encoding.UTF8.GetBytes(refreshToken), refreshValidUntil);

    public byte[] Serialize()
    {
        byte[] data = new byte[checked(44 + AccessToken.Length + RefreshToken.Length)];
        using var stream = new MemoryStream(data);
        using var writer = new BinaryWriter(stream);
        writer.Write(1); // Package format version, independent of AAD v2.
        writer.Write(AccessToken.Length);
        writer.Write(AccessToken);
        writer.Write(RefreshToken.Length);
        writer.Write(RefreshToken);
        writer.Write(AccessTokenValidUntil.Ticks);
        writer.Write(AccessTokenValidUntil.Offset.Ticks);
        writer.Write(RefreshTokenValidUntil.Ticks);
        writer.Write(RefreshTokenValidUntil.Offset.Ticks);
        return data;
    }

    public static SessionTokenPackage Deserialize(byte[] data)
    {
        byte[]? access = null;
        byte[]? refresh = null;
        try
        {
            var remaining = data.AsSpan();
            if (remaining.Length < 44 || ReadInt32(ref remaining) != 1) throw new CryptographicException("Invalid session package.");
            access = ReadToken(ref remaining);
            refresh = ReadToken(ref remaining);
            if (remaining.Length != 32) throw new CryptographicException("Invalid session package.");
            var accessUntil = new DateTimeOffset(ReadInt64(ref remaining), TimeSpan.FromTicks(ReadInt64(ref remaining)));
            var refreshUntil = new DateTimeOffset(ReadInt64(ref remaining), TimeSpan.FromTicks(ReadInt64(ref remaining)));
            return new(access, accessUntil, refresh, refreshUntil);
        }
        catch
        {
            if (access is not null) CryptographicOperations.ZeroMemory(access);
            if (refresh is not null) CryptographicOperations.ZeroMemory(refresh);
            throw new CryptographicException("Invalid session package.");
        }
    }

    private static byte[] ReadToken(ref Span<byte> remaining)
    {
        if (remaining.Length < 4) throw new CryptographicException();
        int length = ReadInt32(ref remaining);
        if (length <= 0 || length > remaining.Length) throw new CryptographicException();
        byte[] value = remaining[..length].ToArray();
        remaining = remaining[length..];
        return value;
    }

    private static int ReadInt32(ref Span<byte> remaining)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(remaining);
        remaining = remaining[4..];
        return value;
    }

    private static long ReadInt64(ref Span<byte> remaining)
    {
        long value = BinaryPrimitives.ReadInt64LittleEndian(remaining);
        remaining = remaining[8..];
        return value;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(AccessToken);
        CryptographicOperations.ZeroMemory(RefreshToken);
    }
}
