using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Migrations;
using xKsef.CredentialService.Security;
using xKsef.CredentialService.Services;
using xKsef.Infrastructure.Credentials;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefSourceCredentialTests
{
    private const string Owner = "user-one";
    private const string OtherUser = "user-two";
    private const string Nip = "0123456789";

    [Fact]
    public async Task OnePrivateCertificateSupportsTwoTenantsWithIndependentSessions()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid id = await service.SaveAsync(null, Owner, "Certificate", [1, 2, 3]);
        var stored = await db.KsefCredentials.AsNoTracking().SingleAsync();
        Assert.Null(stored.TenantId);
        Assert.Null(stored.ContextNip);
        Assert.Equal(new byte[] { 1, 2, 3 }, await service.GetAsync(id, null, Owner, "Certificate"));
        Assert.Null(await service.GetAsync(id, null, OtherUser, "Certificate"));
        Assert.Null(await service.FindCredentialIdAsync(null, OtherUser, "Certificate"));
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Assert.Equal("Certificate", await service.GetCredentialTypeAsync(id, tenantA, Owner, "0123456789"));
        Assert.Equal("Certificate", await service.GetCredentialTypeAsync(id, tenantB, Owner, "0123456789"));
        Assert.Null(await service.GetCredentialTypeAsync(id, tenantB, OtherUser, "0123456789"));
        var until = DateTimeOffset.UtcNow.AddHours(1);
        await service.SaveSessionTokensAsync(tenantA, Owner, id, Nip, "access-A", until, "refresh-A", until);
        await service.SaveSessionTokensAsync(tenantB, Owner, id, "9876543210", "access-B", until, "refresh-B", until);
        Assert.True(await service.HasValidSessionAsync(tenantA, Owner, id, Nip));
        Assert.True(await service.HasValidSessionAsync(tenantB, Owner, id, "9876543210"));
        Assert.Null(await service.GetAccessTokenAsync(tenantB, Owner, id, Nip));
        Assert.Null(await service.GetRefreshTokenAsync(tenantA, Owner, id, "9876543210"));
        Assert.Null(await service.GetRefreshTokenAsync(tenantA, OtherUser, id, Nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSessionTokensAsync(tenantA, OtherUser, id, Nip, "attack", until, "attack", until));
        var sessions = await db.KsefSessions.AsNoTracking().OrderBy(s => s.TenantId).ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, s => Assert.Equal(id, s.CredentialId));
        var aad = SecretEncryptionContext.UserCertificate(id, Owner);
        Assert.Equal(new byte[] { 1, 2, 3 }, new ContextualSecretEncryptionService(store.Protector).Decrypt(Envelope(stored), aad));
        Assert.Single(await db.KsefCredentials.ToListAsync());

        // Moving a valid package between tenant contexts still fails authentication.
        var a = sessions.Single(s => s.TenantId == tenantA);
        var b = sessions.Single(s => s.TenantId == tenantB);
        await db.KsefSessions.Where(s => s.Id == b.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(s => s.EncryptedData, a.EncryptedData).SetProperty(s => s.EncryptedDek, a.EncryptedDek)
            .SetProperty(s => s.Nonce, a.Nonce).SetProperty(s => s.AuthTag, a.AuthTag));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.GetAccessTokenAsync(tenantB, Owner, id, "9876543210"));
    }

    [Theory]
    [InlineData("Certificate", "owner")]
    [InlineData("Certificate", "record")]
    [InlineData("Token", "owner")]
    [InlineData("Token", "record")]
    [InlineData("Token", "tenant")]
    [InlineData("Token", "nip")]
    [InlineData("Token", "type")]
    public async Task SourceAadRejectsTamperedOwnerRecordAndContext(string type, string field)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid? tenant = type == "Token" ? Guid.NewGuid() : null;
        string? nip = type == "Token" ? Nip : null;
        Guid id = await service.SaveAsync(tenant, Owner, type, [1, 2], contextNip: nip);
        string user = Owner;
        if (field == "owner")
        {
            user = OtherUser;
            await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, user));
        }
        else if (field == "record")
        {
            Guid newId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE KsefCredentials SET Id={newId} WHERE Id={id}");
            id = newId;
        }
        else if (field == "tenant")
        {
            tenant = Guid.NewGuid();
            await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.TenantId, tenant));
        }
        else if (field == "nip")
        {
            nip = "9876543210";
            await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ContextNip, nip));
        }
        else
        {
            type = "Certificate";
            tenant = null;
            nip = null;
            await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CredentialType, type).SetProperty(c => c.TenantId, tenant).SetProperty(c => c.ContextNip, nip));
        }
        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.GetAsync(id, tenant, user, type, contextNip: nip));
    }

    [Theory]
    [InlineData("Certificate")]
    [InlineData("Token")]
    public async Task InactiveLatestCredentialIsRejectedWithoutChoosingAnother(string type)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid? tenant = type == "Token" ? Guid.NewGuid() : null;
        string? nip = type == "Token" ? Nip : null;
        Guid oldId = await service.SaveAsync(tenant, Owner, type, [1], contextNip: nip);
        await db.KsefCredentials.Where(c => c.Id == oldId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, DateTimeOffset.UtcNow.AddDays(-1)));
        Guid latestId = await service.SaveAsync(tenant, Owner, type, [2], contextNip: nip);
        await db.KsefCredentials.Where(c => c.Id == latestId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        Assert.Null(await service.FindCredentialIdAsync(tenant, Owner, type, contextNip: nip));
        Assert.Null(await service.GetAsync(latestId, tenant, Owner, type, contextNip: nip));
        Assert.Equal(new byte[] { 1 }, await service.GetAsync(oldId, tenant, Owner, type, contextNip: nip));
    }

    [Fact]
    public async Task InvalidSourceShapesAndMissingIdentityAreRejected()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Guid.NewGuid(), Owner, "Certificate", [1]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(null, Owner, "Certificate", [1], contextNip: Nip));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(null, Owner, "Token", [1], contextNip: Nip));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Guid.Empty, Owner, "Token", [1], contextNip: Nip));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SaveAsync(null, " ", "Certificate", [1]));
        Assert.Empty(await db.KsefCredentials.ToListAsync());
    }

    [Fact]
    public async Task CertificateImporterAndBackendContractRequireOnlyOwner()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        using var rsa = RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=local-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificateService = new KsefCertificateService(new KsefCertificateImporter(), store.CreateService(db));
        Guid id = await certificateService.SaveCertificateAsync(Owner, certificate.ExportCertificatePem(),
            rsa.ExportEncryptedPkcs8PrivateKeyPem("test-only", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000)), "test-only");
        using var loaded = await certificateService.GetCertificateAsync(id, Owner);
        Assert.NotNull(loaded);
        Assert.True(loaded.HasPrivateKey);
        Assert.Null(await certificateService.GetCertificateAsync(id, OtherUser));
        Assert.Null((await db.KsefCredentials.SingleAsync()).TenantId);
        Assert.Null(typeof(xKsef.CredentialService.Models.SaveKsefCertificateRequest).GetProperty("TenantId"));

        var calls = new List<(Uri Uri, JsonElement? Body)>();
        using var http = new HttpClient(new CredentialServiceEnvironmentTests.StubHandler(async (message, _) =>
        {
            JsonElement? body = message.Content is null ? null : JsonSerializer.Deserialize<JsonElement>(await message.Content.ReadAsStringAsync());
            calls.Add((message.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { CredentialId = id, Exists = true }) };
        })) { BaseAddress = new Uri("https://credential.test/") };
        var client = new CredentialServiceClient(new ClientFactory(http));
        await client.SaveCertificateAsync(Owner, "pem", "key", "password");
        await client.GetCertificateInfoAsync(Owner);
        Assert.False(calls[0].Body!.Value.TryGetProperty("tenantId", out _));
        Assert.Equal(Owner, calls[0].Body!.Value.GetProperty("userId").GetString());
        Assert.DoesNotContain("tenant", calls[1].Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetModelHasOnlySourcesAndPreservesSessionOwnerRelation()
    {
        using var context = new CredentialsDbContextFactory().CreateDbContext([]);
        var model = context.GetService<IDesignTimeModel>().Model;
        var credential = model.FindEntityType(typeof(KsefCredential))!;
        Assert.Equal(typeof(Guid?), credential.FindProperty("TenantId")!.ClrType);
        Assert.True(credential.FindProperty("TenantId")!.IsNullable);
        Assert.Null(credential.FindProperty("SourceCredentialId"));
        Assert.Null(credential.FindProperty("ValidUntil"));
        Assert.Equal(new[] { "AuthTag", "ContextNip", "CreatedAt", "CredentialType", "EncryptedData", "EncryptedDek", "Id", "IsActive", "Name", "Nonce", "ProtectionType", "TenantId", "UserId" },
            credential.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(2, credential.GetIndexes().Count());
        Assert.All(credential.GetIndexes(), index => Assert.False(index.IsUnique));
        var checks = credential.GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);
        Assert.Equal(7, checks.Count);
        Assert.Contains("[TenantId] IS NULL AND [ContextNip] IS NULL", checks["CK_KsefCredentials_SourceContext"]);
        Assert.Contains("[TenantId] IS NOT NULL", checks["CK_KsefCredentials_SourceContext"]);
        Assert.Contains("[ContextNip] IS NOT NULL", checks["CK_KsefCredentials_SourceContext"]);
        Assert.Contains("DATALENGTH([ContextNip]) = 20", checks["CK_KsefCredentials_SourceContext"]);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", checks["CK_KsefCredentials_SourceContext"]);
        var session = model.FindEntityType(typeof(KsefSession))!;
        var fk = Assert.Single(session.GetForeignKeys());
        Assert.Equal(new[] { "CredentialId", "UserId" }, fk.Properties.Select(p => p.Name));
        Assert.Equal(new[] { "Id", "UserId" }, fk.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void MigrationOnlyRebuildsSourcesAndDoesNotMigrateDataOrAssignSyntheticTenant()
    {
        using var context = new CredentialsDbContextFactory().CreateDbContext([]);
        var migration = new RebuildKsefSourceCredentials();
        Assert.Equal(15, migration.UpOperations.Count);
        Assert.Equal(15, migration.DownOperations.Count);
        Assert.All(migration.UpOperations.Concat(migration.DownOperations), operation =>
        {
            string? table = operation switch
            {
                DropIndexOperation o => o.Table, CreateIndexOperation o => o.Table,
                DropColumnOperation o => o.Table, AddColumnOperation o => o.Table,
                AlterColumnOperation o => o.Table,
                AddCheckConstraintOperation o => o.Table, DropCheckConstraintOperation o => o.Table,
                _ => throw new InvalidOperationException(operation.GetType().Name)
            };
            Assert.Equal("KsefCredentials", table);
        });
        Assert.Equal(new[] { "SourceCredentialId", "ValidUntil" }, migration.UpOperations.OfType<DropColumnOperation>().Select(o => o.Name));
        var tenant = Assert.Single(migration.UpOperations.OfType<AlterColumnOperation>(), o => o.Name == "TenantId");
        Assert.True(tenant.IsNullable);
        Assert.Null(tenant.DefaultValue);
        Assert.Null(Assert.Single(migration.DownOperations.OfType<AlterColumnOperation>(), o => o.Name == "TenantId").DefaultValue);
        var model = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel, designTime: true);
        foreach (var operations in new[] { migration.UpOperations, migration.DownOperations })
        {
            string sql = string.Join(Environment.NewLine, context.GetService<IMigrationsSqlGenerator>().Generate(operations, model).Select(c => c.CommandText));
            Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|INSERT|DELETE|TRUNCATE)\b", sql);
            Assert.DoesNotContain("KsefSessions", sql);
        }
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static EncryptedSecret Envelope(KsefCredential credential) => new()
    {
        EncryptedData = credential.EncryptedData, EncryptedDek = credential.EncryptedDek, Nonce = credential.Nonce,
        AuthTag = credential.AuthTag, ProtectionType = credential.ProtectionType
    };

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
