using System.ComponentModel;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

internal static class GusBirTransportDiagnostics
{
    internal static GusBirContentType ContentType(HttpContent content) => content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
    {
        null => GusBirContentType.Missing,
        "application/soap+xml" => GusBirContentType.SoapXml,
        "application/xop+xml" => GusBirContentType.XopXml,
        "multipart/related" => GusBirContentType.MultipartRelated,
        "text/html" => GusBirContentType.Html,
        "text/xml" or "application/xml" => GusBirContentType.Xml,
        "application/json" or "application/problem+json" => GusBirContentType.Json,
        _ => GusBirContentType.Other
    };

    internal static GusBirDiagnostics FromException(Exception exception, GusBirFailureStage stage, int? status, GusBirContentType contentType)
    {
        var httpError = exception switch
        {
            HttpRequestException request => (HttpRequestError?)request.HttpRequestError,
            HttpIOException read => read.HttpRequestError,
            _ => null
        };
        var kind = httpError switch
        {
            HttpRequestError.NameResolutionError => GusBirFailureKind.Dns,
            HttpRequestError.ConnectionError => GusBirFailureKind.Connection,
            HttpRequestError.SecureConnectionError => GusBirFailureKind.Tls,
            _ => stage == GusBirFailureStage.ReadResponse ? GusBirFailureKind.ResponseRead : GusBirFailureKind.Unknown
        };
        GusBirSystemErrorKind systemKind = GusBirSystemErrorKind.HResult;
        var systemCode = exception.HResult;
        if (status is null && exception is HttpRequestException { StatusCode: { } exceptionStatus })
            status = (int)exceptionStatus;
        // Inspect only typed codes, never messages/Data/ToString. Bound traversal of arbitrary handler exceptions.
        Exception? cause = exception;
        for (var depth = 0; cause is not null && depth < 16; depth++, cause = cause.InnerException)
        {
            if (cause is SocketException socket)
            {
                systemKind = GusBirSystemErrorKind.Socket;
                systemCode = (int)socket.SocketErrorCode;
                if ((httpError is null or HttpRequestError.Unknown) && stage == GusBirFailureStage.SendRequest && kind != GusBirFailureKind.Tls)
                    kind = socket.SocketErrorCode switch
                    {
                        SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => GusBirFailureKind.Dns,
                        SocketError.ConnectionRefused or SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.TimedOut or
                            SocketError.NetworkDown or SocketError.NetworkUnreachable or SocketError.NetworkReset or SocketError.HostDown or
                            SocketError.HostUnreachable or SocketError.NotConnected => GusBirFailureKind.Connection,
                        _ => GusBirFailureKind.Unknown
                    };
                break;
            }
            if (cause is Win32Exception native)
            {
                systemKind = GusBirSystemErrorKind.Win32;
                systemCode = native.NativeErrorCode;
                break;
            }
            if (cause is AuthenticationException && (httpError is null or HttpRequestError.Unknown))
                kind = GusBirFailureKind.Tls;
        }
        if (kind == GusBirFailureKind.Unknown && status is not null) kind = GusBirFailureKind.Http;
        return new GusBirDiagnostics(kind, stage, httpError, status, contentType, systemKind, systemCode);
    }
}
