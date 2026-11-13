namespace xKsef.Contracts.Authentication;

public sealed record SubmitKsefAuthenticationRequest(Guid OperationId, string SignedXml);