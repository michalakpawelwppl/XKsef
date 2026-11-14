using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using xKsef.CredentialService.Security;

namespace xKsef.Tests.Infrastructure;

public sealed class ContextualSecretEncryptionTests
{
    private static readonly Guid RecordId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
    private static readonly Guid TenantId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly Guid CredentialId = Guid.Parse("87654321-4321-4321-4321-cba987654321");
    private const string UserId = " User-Case ";
    private const string Nip = "0123456789";

    private static SecretEncryptionContext Context(int kind, string user = UserId, Guid? tenant = null, Guid? record = null, Guid? credential = null, string nip = Nip) => kind switch
    {
        0 => SecretEncryptionContext.UserCertificate(record ?? RecordId, user),
        1 => SecretEncryptionContext.KsefToken(record ?? RecordId, user, tenant ?? TenantId, nip),
        2 => SecretEncryptionContext.KsefSession(record ?? RecordId, user, tenant ?? TenantId, credential ?? CredentialId, nip),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RoundTripUsesIndependentRandomKeysAndNonces(int kind)
    {
        var protector = new RecordingKeyProtector();
        var service = new ContextualSecretEncryptionService(protector);
        byte[] input = Encoding.UTF8.GetBytes("private key or token pair");
        var first = service.Encrypt(input, Context(kind));
        var second = service.Encrypt(input, Context(kind));
        Assert.Equal(input, service.Decrypt(first, Context(kind)));
        Assert.Equal(input, service.Decrypt(second, Context(kind)));
        Assert.Equal("DPAPI-CurrentUser", first.ProtectionType);
        Assert.Equal(12, first.Nonce.Length);
        Assert.Equal(16, first.AuthTag.Length);
        Assert.Equal(32, first.EncryptedDek.Length);
        Assert.False(first.Nonce.SequenceEqual(second.Nonce));
        Assert.False(first.EncryptedDek.SequenceEqual(second.EncryptedDek));
        Assert.False(first.EncryptedData.SequenceEqual(input));
        Assert.All(protector.KeyBuffers, key => Assert.All(key, value => Assert.Equal((byte)0, value)));
    }

    [Theory]
    [InlineData(0, "en-US")]
    [InlineData(1, "pl-PL")]
    [InlineData(2, "ar-SA")]
    [InlineData(0, "tr-TR")]
    [InlineData(1, "ar-SA")]
    [InlineData(2, "pl-PL")]
    public void AadMatchesExactUtf8ContractIndependentlyOfCulture(int kind, string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            const string prefix = "[\"xKsef\",2,";
            string expected = kind switch
            {
                0 => prefix + "\"UserCertificate\",\"abcdef01-2345-6789-abcd-ef0123456789\",\" User-Case \"]",
                1 => prefix + "\"KsefToken\",\"abcdef01-2345-6789-abcd-ef0123456789\",\" User-Case \",\"12345678-1234-1234-1234-123456789abc\",\"0123456789\"]",
                _ => prefix + "\"KsefSession\",\"abcdef01-2345-6789-abcd-ef0123456789\",\" User-Case \",\"12345678-1234-1234-1234-123456789abc\",\"87654321-4321-4321-4321-cba987654321\",\"0123456789\"]"
            };
            AssertCanDecryptWithExactAad(Context(kind), expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void AadEscapesUserIdWithoutNormalizingItsValue()
    {
        var context = Context(0, user: " A\"\\\ną ");
        AssertCanDecryptWithExactAad(context, "[\"xKsef\",2,\"UserCertificate\",\"abcdef01-2345-6789-abcd-ef0123456789\",\" A\\u0022\\\\\\n\\u0105 \"]");
    }

    private static void AssertCanDecryptWithExactAad(SecretEncryptionContext context, string expected)
    {
        var service = new ContextualSecretEncryptionService(new RecordingKeyProtector());
        byte[] input = [1, 2, 3, 4];
        var encrypted = service.Encrypt(input, context);
        byte[] result = new byte[input.Length];
        // The test protector stores a copy of the DEK. This bypasses production AAD construction.
        using var aes = new AesGcm(encrypted.EncryptedDek, 16);
        aes.Decrypt(encrypted.Nonce, encrypted.EncryptedData, encrypted.AuthTag, result, Encoding.UTF8.GetBytes(expected));
        Assert.Equal(input, result);
    }

    public static IEnumerable<object[]> ContextTamperingCases()
    {
        for (int kind = 0; kind < 3; kind++)
        {
            yield return [kind, "user"];
            yield return [kind, "record"];
            if (kind != 0)
            {
                yield return [kind, "tenant"];
                yield return [kind, "nip"];
            }
        }
        yield return [2, "credential"];
    }

    [Theory]
    [MemberData(nameof(ContextTamperingCases))]
    public void RejectsChangedAuthenticatedContext(int kind, string field)
    {
        var protector = new RecordingKeyProtector();
        var service = new ContextualSecretEncryptionService(protector);
        var encrypted = service.Encrypt([1, 2, 3], Context(kind));
        var changed = Context(kind,
            user: field == "user" ? "user-case" : UserId,
            record: field == "record" ? Guid.NewGuid() : RecordId,
            tenant: field == "tenant" ? Guid.NewGuid() : TenantId,
            credential: field == "credential" ? Guid.NewGuid() : CredentialId,
            nip: field == "nip" ? "9876543210" : Nip);
        Assert.Throws<AuthenticationTagMismatchException>(() => service.Decrypt(encrypted, changed));
        Assert.All(protector.KeyBuffers, key => Assert.All(key, value => Assert.Equal((byte)0, value)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void RejectsOtherRecordTypes(int source, int target)
    {
        var service = new ContextualSecretEncryptionService(new RecordingKeyProtector());
        var encrypted = service.Encrypt([9], Context(source));
        Assert.Throws<AuthenticationTagMismatchException>(() => service.Decrypt(encrypted, Context(target)));
    }

    [Fact]
    public void CertificateDoesNotDependOnTenantAndCannotAcceptTenantContext()
    {
        var factory = typeof(SecretEncryptionContext).GetMethod(nameof(SecretEncryptionContext.UserCertificate))!;
        Assert.Equal(new[] { typeof(Guid), typeof(string) }, factory.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(typeof(SecretEncryptionContext).GetConstructors());
        var service = new ContextualSecretEncryptionService(new RecordingKeyProtector());
        var encrypted = service.Encrypt([42], SecretEncryptionContext.UserCertificate(CredentialId, UserId));
        // Neither the original nor a newly selected tenant participates in either context.
        foreach (var selectedTenant in new[] { TenantId, Guid.NewGuid() })
        {
            Assert.NotEqual(Guid.Empty, selectedTenant);
            Assert.Equal(new byte[] { 42 }, service.Decrypt(encrypted, SecretEncryptionContext.UserCertificate(CredentialId, UserId)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("123456789 ")]
    [InlineData("１２３４５６７８９０")]
    [InlineData("١٢٣٤٥٦٧٨٩٠")]
    public void RejectsNonAsciiOrInvalidNip(string? nip)
    {
        Assert.Throws<ArgumentException>(() => Context(1, nip: nip!));
        Assert.Throws<ArgumentException>(() => Context(2, nip: nip!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsMissingUserOrRecordIdentity(int kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => Context(kind, user: null!));
        Assert.ThrowsAny<ArgumentException>(() => Context(kind, user: " \t"));
        Assert.Throws<ArgumentException>(() => Context(kind, record: Guid.Empty));
        if (kind != 0) Assert.Throws<ArgumentException>(() => Context(kind, tenant: Guid.Empty));
        if (kind == 2) Assert.Throws<ArgumentException>(() => Context(kind, credential: Guid.Empty));
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("key")]
    public void RejectsTamperedEncryptedPayload(string field)
    {
        var service = new ContextualSecretEncryptionService(new RecordingKeyProtector());
        var encrypted = service.Encrypt([1, 2, 3], Context(2));
        byte[] buffer = field switch
        {
            "ciphertext" => encrypted.EncryptedData,
            "nonce" => encrypted.Nonce,
            "tag" => encrypted.AuthTag,
            _ => encrypted.EncryptedDek
        };
        buffer[0] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => service.Decrypt(encrypted, Context(2)));
    }

    [Fact]
    public void RejectsMalformedEnvelopeAndNonAes256Key()
    {
        var service = new ContextualSecretEncryptionService(new RecordingKeyProtector());
        var encrypted = service.Encrypt([1], Context(0));
        encrypted.ProtectionType = "other";
        Assert.Throws<NotSupportedException>(() => service.Decrypt(encrypted, Context(0)));
        encrypted.ProtectionType = "DPAPI-CurrentUser";
        encrypted.EncryptedDek = new byte[16];
        Assert.Throws<CryptographicException>(() => service.Decrypt(encrypted, Context(0)));
        encrypted.Nonce = new byte[11];
        Assert.Throws<CryptographicException>(() => service.Decrypt(encrypted, Context(0)));
    }

    [Fact]
    public void DoesNotFallBackToLegacyAad()
    {
        var protector = new RecordingKeyProtector();
        var oldService = new SecretEncryptionService(protector);
        var service = new ContextualSecretEncryptionService(protector);
        var encrypted = oldService.Encrypt([1, 2], TenantId, RecordId);
        for (int kind = 0; kind < 3; kind++)
            Assert.Throws<AuthenticationTagMismatchException>(() => service.Decrypt(encrypted, Context(kind)));
    }

    [Fact]
    public void ClearsDekEvenWhenKeyProtectionFails()
    {
        var protector = new RecordingKeyProtector { FailProtection = true };
        var service = new ContextualSecretEncryptionService(protector);
        Assert.Throws<CryptographicException>(() => service.Encrypt([1], Context(0)));
        Assert.All(Assert.Single(protector.KeyBuffers), value => Assert.Equal((byte)0, value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RoundTripsWithRealDpapiCurrentUser(int kind)
    {
        Assert.True(OperatingSystem.IsWindows(), "The CredentialService DPAPI test requires Windows.");
        var service = new ContextualSecretEncryptionService(new DpapiKeyProtector());
        byte[] input = RandomNumberGenerator.GetBytes(64);
        byte[]? result = null;
        try
        {
            var encrypted = service.Encrypt(input, Context(kind));
            Assert.NotEqual(32, encrypted.EncryptedDek.Length);
            result = service.Decrypt(encrypted, Context(kind));
            Assert.Equal(input, result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            if (result is not null) CryptographicOperations.ZeroMemory(result);
        }
    }

    private sealed class RecordingKeyProtector : IKeyProtector
    {
        public List<byte[]> KeyBuffers { get; } = [];
        public bool FailProtection { get; init; }

        public byte[] Protect(byte[] key)
        {
            KeyBuffers.Add(key);
            if (FailProtection) throw new CryptographicException("Test protection failure.");
            return key.ToArray();
        }

        public byte[] Unprotect(byte[] protectedKey)
        {
            byte[] key = protectedKey.ToArray();
            KeyBuffers.Add(key);
            return key;
        }
    }
}
