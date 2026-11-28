namespace xKsef.Domain.SalesInvoices;

public sealed class SalesInvoiceParticipant
{
    private SalesInvoiceParticipant() { }
    internal SalesInvoiceParticipant(Guid tenantId, Guid invoiceId, Guid id) { TenantId = tenantId; SalesInvoiceId = invoiceId; Id = id; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public int Ordinal { get; private set; }
    public InvoiceParticipantRole? Role { get; private set; }
    public string? OtherRoleDescription { get; private set; }
    public decimal? SharePercent { get; private set; }
    public SalesInvoicePartyData Data { get; private set; } = new();
    internal void Apply(InvoiceParticipantData input, int ordinal)
    {
        Ordinal = ordinal; Role = input.Role; OtherRoleDescription = input.OtherRoleDescription; SharePercent = input.SharePercent; Data = input.Party.Freeze();
    }
    public InvoiceParticipantData Snapshot() => new() { Id = Id, Role = Role, OtherRoleDescription = OtherRoleDescription, SharePercent = SharePercent, Party = Data.Freeze() };
}
