using System.Net;

namespace xKsef.Wpf.Models;

public sealed record TenantCreationResult(HttpStatusCode StatusCode, Guid? TenantId, IReadOnlyList<string> Errors);

public sealed record TenantCreationDialogResult(Guid TenantId, long SessionVersion);
