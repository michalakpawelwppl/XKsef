namespace xKsef.Contracts.Authentication;

public sealed record PrepareKsefAuthenticationResponse(Guid OperationId, string Xml);