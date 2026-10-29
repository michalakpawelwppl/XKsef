using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using xKsef.Application.Invoices.Pdf;

namespace xKsef.Infrastructure.Invoices.Pdf;

public sealed class NodeInvoicePdfGenerator : IInvoicePdfGenerator
{
    private readonly string _nodeExecutable;
    private readonly string _scriptPath;

    public NodeInvoicePdfGenerator(IConfiguration configuration)
    {
        _nodeExecutable = configuration["PdfGenerator:NodeExecutable"]
            ?? throw new InvalidOperationException(
                "Brak konfiguracji PdfGenerator:NodeExecutable");

        _scriptPath = configuration["PdfGenerator:ScriptPath"]
            ?? throw new InvalidOperationException(
                "Brak konfiguracji PdfGenerator:ScriptPath");
    }

    public async Task<byte[]> GenerateAsync(
        byte[] invoiceXml,
        string ksefNumber,
        CancellationToken cancellationToken = default)
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "xKsef",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);

        string inputPath = Path.Combine(tempDirectory, "invoice.xml");
        string outputPath = Path.Combine(tempDirectory, "invoice.pdf");

        try
        {
            await File.WriteAllBytesAsync(
                inputPath,
                invoiceXml,
                cancellationToken);

            await RunNodeAsync(
                _nodeExecutable,
                _scriptPath,
                inputPath,
                outputPath,
                ksefNumber,
                cancellationToken);


            return await File.ReadAllBytesAsync(
                outputPath,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static async Task RunNodeAsync(
string nodeExecutable,
string scriptPath,
    string inputPath,
    string outputPath,
    string ksefNumber,
    CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)
                ?? throw new InvalidOperationException(
                    "Nie można ustalić katalogu generatora PDF."),

            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(ksefNumber);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);

            throw;
        }

        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Generator PDF zakończył się błędem: {error}");
        }
    }
}