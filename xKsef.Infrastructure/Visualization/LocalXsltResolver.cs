using System.Net;
using System.Xml;

namespace xKsef.Infrastructure.Invoices.Visualization;

public sealed class LocalXsltResolver : XmlResolver
{
    private readonly string _allowedDirectory;

    public LocalXsltResolver(string allowedDirectory)
    {
        _allowedDirectory = Path.GetFullPath(allowedDirectory);
    }

    public override ICredentials? Credentials
    {
        set { }
    }

    public override object GetEntity(
        Uri absoluteUri,
        string? role,
        Type? ofObjectToReturn)
    {
        if (!absoluteUri.IsFile)
        {
            throw new XmlException(
                "Dozwolone są wyłącznie lokalne pliki XSLT.");
        }

        string filePath = Path.GetFullPath(
            absoluteUri.LocalPath);

        string relativePath = Path.GetRelativePath(
            _allowedDirectory,
            filePath);

        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new XmlException(
                "Plik XSLT znajduje się poza dozwolonym katalogiem.");
        }

        return File.OpenRead(filePath);
    }
}