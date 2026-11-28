using xKsef.Api.Hosting;
using xKsef.Api.Authentication;
using xKsef.Infrastructure.Gus;
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
builder.Services.AddGusBirInfrastructure(builder.Configuration);
builder.Services.AddScoped<TenantAuthorizationFilter>();
builder.Services.AddScoped<CredentialManagementAuthorizationFilter>();

builder.Services.AddExceptionHandler<BrowserSessionExceptionHandler>();
builder.Services.AddExceptionHandler<SalesInvoiceExceptionHandler>();
builder.Services.AddExceptionHandler<InvoiceNumberSeriesExceptionHandler>();
builder.Services.AddExceptionHandler<DuplicateInvoiceExceptionHandler>();
builder.Services.AddExceptionHandler<ExternalServiceRateLimitExceptionHandler>();
builder.Services.AddExceptionHandler<GusExceptionHandler>();
builder.Services.AddExceptionHandler<CredentialServiceExceptionHandler>();
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);

builder.Services.AddSingleton<IInvoiceHtmlTransformer,KsefInvoiceXsltTransformer>();

builder.Services.AddApplicationOperatorAuthorization();
builder.Services.AddRegistrationRateLimiting();

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
app.UseBrowserFrontend();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
