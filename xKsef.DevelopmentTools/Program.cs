using xKsef.Infrastructure.Ksef.Authentication;

string nip = "1234567890"; // Przykładowy NIP testowy
string path = @"C:\KsefTest\certificate.pfx";

Directory.CreateDirectory(
    Path.GetDirectoryName(path)!);

using var certificate =
    TestCertificateGenerator.Generate(nip);

TestCertificateGenerator.Save(
    certificate,
    path,
    "HasloTylkoDoLokalnegoTestu");

Console.WriteLine($"Certyfikat: {path}");
Console.WriteLine($"Klucz prywatny: {certificate.HasPrivateKey}");