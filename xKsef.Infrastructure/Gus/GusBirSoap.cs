using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;
using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

internal static class GusBirSoap
{
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;
    internal static readonly XNamespace Service = "http://CIS/BIR/PUBL/2014/07";
    internal static readonly XNamespace Diagnostic = "http://CIS/BIR/2014/07";
    internal static readonly XNamespace Data = "http://CIS/BIR/PUBL/2014/07/DataContract";
    internal static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Addressing = "http://www.w3.org/2005/08/addressing";

    internal static HttpRequestMessage CreateRequest(Uri endpoint, string operation, XElement parameter, string? sid)
    {
        var service = operation == "GetValue" ? Diagnostic : Service;
        var action = service.NamespaceName + (operation == "GetValue" ? "/IUslugaBIR/" : "/IUslugaBIRzewnPubl/") + operation;
        var envelope = new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", Soap), new XAttribute(XNamespace.Xmlns + "a", Addressing),
            new XElement(Soap + "Header",
                new XElement(Addressing + "Action", new XAttribute(Soap + "mustUnderstand", "1"), action),
                new XElement(Addressing + "To", new XAttribute(Soap + "mustUnderstand", "1"), endpoint.AbsoluteUri)),
            new XElement(Soap + "Body", new XElement(service + operation, parameter)));
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/soap+xml") };
        request.Content.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("action", '"' + action + '"'));
        if (sid is not null)
            request.Headers.Add("sid", sid);
        return request;
    }

    internal static async Task<string> ReadResultAsync(HttpContent content, string operation, CancellationToken cancellationToken)
    {
        var mediaType = content.Headers.ContentType;
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw Invalid(operation);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadBoundedAsync(stream, cancellationToken);
        XDocument document;
        if (string.Equals(mediaType?.MediaType, "multipart/related", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = Parameter(mediaType!, "boundary");
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
                throw Invalid(operation);
            var start = Parameter(mediaType!, "start")?.Trim('<', '>');
            var multipart = new MultipartReader(boundary, new MemoryStream(bytes)) { BodyLengthLimit = MaximumResponseBytes };
            MultipartSection? section;
            document = null!;
            while ((section = await multipart.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                var id = section.Headers?.TryGetValue("Content-ID", out var values) == true ? values.ToString().Trim('<', '>') : null;
                if (start is not null && id != start)
                    continue;
                if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var partType) || !IsXml(partType.MediaType))
                    throw Invalid(operation);
                document = Parse(await ReadBoundedAsync(section.Body, cancellationToken));
                break;
            }
            if (document is null)
                throw Invalid(operation);
        }
        else
        {
            if (!IsXml(mediaType?.MediaType))
                throw Invalid(operation);
            document = Parse(bytes);
        }
        if (document.Root?.Name != Soap + "Envelope")
            throw Invalid(operation);
        var bodies = document.Root.Elements(Soap + "Body").ToArray();
        if (bodies.Length != 1)
            throw Invalid(operation);
        var elements = bodies[0].Elements().ToArray();
        if (elements.Length != 1)
            throw Invalid(operation);
        if (elements[0].Name == Soap + "Fault")
            throw new GusBirException(GusBirError.ServiceFault, operation);
        var service = operation == "GetValue" ? Diagnostic : Service;
        if (elements[0].Name != service + (operation + "Response"))
            throw Invalid(operation);
        var results = elements[0].Elements(service + (operation + "Result")).ToArray();
        if (results.Length != 1 || results[0].HasElements)
            throw Invalid(operation);
        return results[0].Value;
    }

    internal static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings());
        return XDocument.Load(reader);
    }

    private static XDocument Parse(byte[] xml)
    {
        using var stream = new MemoryStream(xml);
        using var reader = XmlReader.Create(stream, ReaderSettings());
        return XDocument.Load(reader);
    }

    private static XmlReaderSettings ReaderSettings() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumResponseBytes };
    private static bool IsXml(string? mediaType) => string.Equals(mediaType, "application/soap+xml", StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, "application/xop+xml", StringComparison.OrdinalIgnoreCase);
    private static string? Parameter(MediaTypeHeaderValue mediaType, string name) => mediaType.Parameters.FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');
    private static GusBirException Invalid(string operation) => new(GusBirError.InvalidResponse, operation);

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (result.Length + count > MaximumResponseBytes)
                throw new InvalidDataException("GUS response size exceeded.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
