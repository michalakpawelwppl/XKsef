using System.Net.Http;

namespace xKsef.Application.Gus;

public enum GusBirFailureKind
{
    Unknown,
    Dns,
    Connection,
    Tls,
    Http,
    ResponseRead,
    InvalidResponse,
    SoapFault,
    Timeout,
    Xml,
    Mtom,
    SoapValidation
}

public enum GusBirFailureStage
{
    SendRequest,
    ValidateHttpResponse,
    ReadResponse,
    ParseXml,
    ParseMtom,
    ValidateSoap,
    ValidateContentType,
    ValidateResult
}

public enum GusBirContentType
{
    Missing,
    SoapXml,
    XopXml,
    MultipartRelated,
    Html,
    Xml,
    Json,
    Other
}

public enum GusBirSystemErrorKind
{
    Socket,
    Win32,
    HResult
}

public enum GusBirSoapFaultCode
{
    None,
    Other,
    Sender,
    Receiver,
    VersionMismatch,
    MustUnderstand,
    DataEncodingUnknown
}

public enum GusBirSoapFaultSubcode
{
    None,
    Other,
    ActionNotSupported,
    DestinationUnreachable,
    ActionMismatch,
    MessageAddressingHeaderRequired,
    InvalidAddressingHeader,
    EndpointUnavailable
}

/// <summary>Only controlled enums and numeric metadata. Never contains headers, payloads or original exceptions.</summary>
public sealed record GusBirDiagnostics(
    GusBirFailureKind Kind,
    GusBirFailureStage Stage,
    HttpRequestError? HttpRequestError = null,
    int? HttpStatusCode = null,
    GusBirContentType ContentType = GusBirContentType.Missing,
    GusBirSystemErrorKind? SystemErrorKind = null,
    int? SystemErrorCode = null)
{
    public GusBirSoapFaultCode SoapFaultCode { get; init; }
    public GusBirSoapFaultSubcode SoapFaultSubcode { get; init; }
}
