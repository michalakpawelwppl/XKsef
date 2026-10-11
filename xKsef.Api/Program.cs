using xKsef.Application.Invoices;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Invoices;
using xKsef.Infrastructure.Ksef.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

//builder.Services.AddScoped<IInvoiceService, FakeInvoiceService>();
builder.Services.AddKsefInfrastructure(
    builder.Configuration);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    var tokenProvider = app.Services
//        .GetRequiredService<KsefTokenProvider>();

//    var token1 = await tokenProvider.GetAccessTokenAsync();

//    app.Logger.LogInformation(
//        "Pierwsze pobranie tokena: {Result}",
//        string.IsNullOrEmpty(token1) ? "BRAK" : "OK");

//    var token2 = await tokenProvider.GetAccessTokenAsync();

//    app.Logger.LogInformation(
//        "Drugie pobranie tokena: {Result}",
//        string.IsNullOrEmpty(token2) ? "BRAK" : "OK");

//    app.Logger.LogInformation(
//        "Czy otrzymaliśmy ten sam token: {Result}",
//        token1 == token2);
//}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
