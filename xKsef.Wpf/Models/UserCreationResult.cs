using System.Net;

namespace xKsef.Wpf.Models;

public sealed record UserCreationResult(HttpStatusCode StatusCode, string? UserId, IReadOnlyList<string> Errors);
