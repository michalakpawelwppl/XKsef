using KSeF.Client.Api.Services;
using KSeF.Client.Api.Services.Internal;
using KSeF.Client.Clients;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using xKsef.CredentialService.Cryptography;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;
using xKsef.CredentialService.Services;

var builder = WebApplication.CreateBuilder(args);

CryptoConfig.AddAlgorithm(typeof(EcdsaSha256SignatureDescription), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256");


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
builder.Services.AddSingleton<ISecretEncryptionService, SecretEncryptionService>();
builder.Services.AddDbContext<CredentialsDbContext>(options => 
    options.UseSqlServer(builder.Configuration.GetConnectionString("CredentialsDatabase")));
builder.Services.AddScoped<IKsefCredentialService, KsefCredentialService>();
string trustedThumbprint = builder.Configuration["ClientCertificate:Thumbprint"]?.Replace(" ", "").ToUpperInvariant() ?? throw new InvalidOperationException("Nie skonfigurowano certyfikatu klienta.");

using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
store.Open(OpenFlags.ReadOnly);

X509Certificate2? trustedCertificate = store.Certificates.Find(X509FindType.FindByThumbprint, trustedThumbprint, validOnly: false).OfType<X509Certificate2>().FirstOrDefault();

if (trustedCertificate == null) throw new InvalidOperationException("Nie znaleziono zaufanego certyfikatu klienta.");

builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme).AddCertificate(options =>
{
    options.AllowedCertificateTypes = CertificateTypes.All;
    options.RevocationMode = X509RevocationMode.NoCheck;
    options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
    options.CustomTrustStore = new X509Certificate2Collection(trustedCertificate);
    options.Events = new CertificateAuthenticationEvents
    {
        OnCertificateValidated = context =>
        {
            byte[] expected = Convert.FromHexString(trustedThumbprint);
            byte[] actual = context.ClientCertificate.GetCertHash(HashAlgorithmName.SHA1);

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                context.Fail("Niezaufany certyfikat klienta.");
                return Task.CompletedTask;
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "xKsef.Api"),
                new Claim(ClaimTypes.Name, "xKsef.Api")
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CertificateAuthenticationDefaults.AuthenticationScheme));
            context.Success();
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        httpsOptions.ClientCertificateValidation = (certificate, chain, errors) =>
        {
            if (certificate == null) return false;

            byte[] expected = Convert.FromHexString(trustedThumbprint);
            byte[] actual = certificate.GetCertHash(HashAlgorithmName.SHA1);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        };
    });
});
builder.Services.AddKSeFClient(options =>
{
    options.BaseUrl = builder.Configuration["Ksef:BaseUrl"] ?? throw new InvalidOperationException("Nie skonfigurowano adresu KSeF.");
});
builder.Services.AddTransient<ICryptographyClient, CryptographyClient>();
builder.Services.AddSingleton<ICertificateFetcher, DefaultCertificateFetcher>();
builder.Services.AddSingleton<ICryptographyService, CryptographyService>();
builder.Services.AddScoped<IKsefTokenAuthenticationService, KsefTokenAuthenticationService>();
builder.Services.AddSingleton<KsefAuthenticationLock>();
builder.Services.AddScoped<KsefCertificateImporter>();
builder.Services.AddScoped<KsefCertificateService>();
builder.Services.AddScoped<KsefCertificateAuthenticationService>();
builder.Services.AddScoped<IKsefCertificateAuthenticationService>(sp => sp.GetRequiredService<KsefCertificateAuthenticationService>());

builder.Services.AddScoped<KsefTokenRefreshService>();
builder.Services.AddScoped<IKsefSessionService, KsefSessionService>();
builder.Services.AddScoped<IKsefCertificateAuthenticationService, KsefCertificateAuthenticationService>();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var cryptographyService = scope.ServiceProvider.GetRequiredService<ICryptographyService>();
    await cryptographyService.WarmupAsync();
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/internal/health", (ClaimsPrincipal user) =>
{
    return Results.Ok(new { Success = true, Client = user.Identity?.Name });
}).RequireAuthorization();

app.Run();
