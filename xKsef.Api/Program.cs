using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using xKsef.Api.Authorization;
using xKsef.Api.ExceptionHandlers;
using xKsef.Application.Invoices.Visualization;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Invoices.Visualization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

//builder.Services.AddScoped<IInvoiceService, FakeInvoiceService>();
builder.Services.AddKsefInfrastructure(builder.Configuration);
builder.Services.AddDatabaseInfrastructure(builder.Configuration);
builder.Services.AddScoped<TenantAuthorizationFilter>();
builder.Services.AddScoped<CredentialManagementAuthorizationFilter>();

builder.Services.AddExceptionHandler<DuplicateInvoiceExceptionHandler>();
builder.Services.AddExceptionHandler<ExternalServiceRateLimitExceptionHandler>();
builder.Services.AddExceptionHandler<CredentialServiceExceptionHandler>();
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Brak Jwt:Issuer.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Brak Jwt:Audience.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Brak Jwt:SigningKey.");
var signingKeyBytes = Convert.FromBase64String(jwtSigningKey);
if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Klucz JWT musi mieć co najmniej 256 bitów.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateLifetime = true,
            RequireExpirationTime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                signingKeyBytes),

            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddSingleton<IInvoiceHtmlTransformer,KsefInvoiceXsltTransformer>();

builder.Services.AddApplicationOperatorAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey: ipAddress, factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddHttpClient("CredentialService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["CredentialService:BaseUrl"] ?? throw new InvalidOperationException("Brak adresu CredentialService."));
    client.Timeout = Timeout.InfiniteTimeSpan; // The environment handler owns the same 100-second deadline.
}).AddHttpMessageHandler<CredentialServiceEnvironmentHandler>().ConfigurePrimaryHttpMessageHandler(() =>
{
    string thumbprint = builder.Configuration["CredentialService:ClientCertificateThumbprint"]?.Replace(" ", "").ToUpperInvariant() ?? throw new InvalidOperationException("Brak Thumbprint certyfikatu klienta.");

    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly);

    X509Certificate2? certificate = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).OfType<X509Certificate2>().FirstOrDefault();

    if (certificate == null || !certificate.HasPrivateKey) throw new InvalidOperationException("Nie znaleziono certyfikatu klienta z kluczem prywatnym.");

    var handler = new HttpClientHandler();
    handler.ClientCertificateOptions = ClientCertificateOption.Manual;
    handler.ClientCertificates.Add(certificate);

    return handler;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentUserSeeder>();
    var password = app.Configuration["DevelopmentUser:Password"] ?? throw new InvalidOperationException("Brak konfiguracji DevelopmentUser:Password.");
    await seeder.SeedAsync(password);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
