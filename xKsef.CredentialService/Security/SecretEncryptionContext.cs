using System.Text.Json;

namespace xKsef.CredentialService.Security;

/// <summary>Immutable authenticated identity of a v2 secret. Certificate contexts have no tenant.</summary>
public sealed class SecretEncryptionContext
{
    private readonly string _recordType;
    private readonly Guid _recordId;
    private readonly string _userId;
    private readonly Guid? _tenantId;
    private readonly Guid? _credentialId;
    private readonly string? _contextNip;

    private SecretEncryptionContext(string recordType, Guid recordId, string userId, Guid? tenantId = null, Guid? credentialId = null, string? contextNip = null)
    {
        if (recordId == Guid.Empty) throw new ArgumentException("Record identifier is required.", nameof(recordId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _recordType = recordType;
        _recordId = recordId;
        _userId = userId;
        _tenantId = tenantId;
        _credentialId = credentialId;
        _contextNip = contextNip;
    }

    public static SecretEncryptionContext UserCertificate(Guid credentialId, string userId) => new("UserCertificate", credentialId, userId);

    public static SecretEncryptionContext KsefToken(Guid credentialId, string userId, Guid tenantId, string contextNip)
    {
        ValidateTenantContext(tenantId, contextNip);
        return new("KsefToken", credentialId, userId, tenantId, contextNip: contextNip);
    }

    public static SecretEncryptionContext KsefSession(Guid sessionId, string userId, Guid tenantId, Guid credentialId, string contextNip)
    {
        ValidateTenantContext(tenantId, contextNip);
        if (credentialId == Guid.Empty) throw new ArgumentException("Credential identifier is required.", nameof(credentialId));
        return new("KsefSession", sessionId, userId, tenantId, credentialId, contextNip);
    }

    // Explicit field order, fixed JSON escaping and UTF-8: never serialize an EF entity or use the current culture.
    internal byte[] CreateAad()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default }))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("xKsef");
            writer.WriteNumberValue(2);
            writer.WriteStringValue(_recordType);
            writer.WriteStringValue(_recordId.ToString("D"));
            writer.WriteStringValue(_userId);
            if (_tenantId.HasValue) writer.WriteStringValue(_tenantId.Value.ToString("D"));
            if (_credentialId.HasValue) writer.WriteStringValue(_credentialId.Value.ToString("D"));
            if (_contextNip is not null) writer.WriteStringValue(_contextNip);
            writer.WriteEndArray();
        }
        return stream.ToArray();
    }

    private static void ValidateTenantContext(Guid tenantId, string contextNip)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant identifier is required.", nameof(tenantId));
        if (contextNip is null || contextNip.Length != 10 || contextNip.Any(character => character < '0' || character > '9'))
            throw new ArgumentException("Context NIP must contain exactly ten ASCII digits.", nameof(contextNip));
    }
}
