namespace xKsef.Application.Gus;

/// <summary>
/// Backend registry lookup. Searches log in automatically and reuse the process-local session.
/// LogoutAsync explicitly ends the shared session; it must not be called after every lookup.
/// Results are proposals for review, not verified contractor records. No persistence is performed.
/// Every SOAP call requires a separate central rate-limit permit within the lookup's shared waiting budget.
/// </summary>
public interface IGusBirClient
{
    Task LoginAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GusEntity>> SearchByNipAsync(string nip, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GusEntity>> SearchByRegonAsync(string regon, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GusEntity>> SearchByKrsAsync(string krsNumber, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
