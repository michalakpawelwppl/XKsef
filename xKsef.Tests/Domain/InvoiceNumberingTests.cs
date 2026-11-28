using xKsef.Domain.InvoiceNumberSeries;

namespace xKsef.Tests.Domain;

public sealed class InvoiceNumberingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static InvoiceNumberReservation Reservation() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "AAAAAAAAAAA=", Guid.NewGuid(), 1, 2026, 1, "FV/000001/2026", new(2026, 11, 27), Guid.NewGuid(), "creator", Now, "NewCounterValue");
    private static VerifiedInvoiceIssuanceOutcome Evidence(InvoiceNumberReservation r, bool accepted) => new(r.TenantId, r.InvoiceId, r.OperationId, r.Id, r.ApprovalId, r.FullNumber, accepted ? InvoiceIssuanceOutcomeKind.Accepted : InvoiceIssuanceOutcomeKind.DefinitelyNotAccepted, "TEST-ONLY-EVIDENCE", accepted ? "TEST-ONLY-KSEF-NUMBER" : null, Now);

    [Fact]
    public void CounterStartsAtConfigurationAndHasNoInvoiceKindDimension()
    {
        var counter = new InvoiceNumberCounter(Guid.NewGuid(), Guid.NewGuid(), 2026, 7, "creator", Now);
        Assert.Equal(7, counter.TakeNext("one", Now)); Assert.Equal(8, counter.TakeNext("two", Now)); Assert.Equal(9, counter.NextValue);
        Assert.Equal(7, counter.StartingNumber); Assert.Equal("two", counter.UpdatedBy);
        Assert.Equal(2026, InvoiceNumberCounter.PeriodFor(NumberSeriesResetMode.Yearly, new(2026, 12, 31)));
        Assert.Equal(2027, InvoiceNumberCounter.PeriodFor(NumberSeriesResetMode.Yearly, new(2027, 1, 1)));
        Assert.Equal(0, InvoiceNumberCounter.PeriodFor(NumberSeriesResetMode.None, new(2026, 12, 31)));
        Assert.Equal(0, InvoiceNumberCounter.PeriodFor(NumberSeriesResetMode.None, new(2027, 1, 1)));
    }
    [Fact]
    public void CounterExhaustionCannotWrapOrReset()
    {
        var counter = new InvoiceNumberCounter(Guid.NewGuid(), Guid.NewGuid(), 0, long.MaxValue, "user", Now);
        Assert.Equal(long.MaxValue, counter.TakeNext("user", Now)); Assert.Null(counter.NextValue);
        Assert.Throws<InvoiceNumberingConflictException>(() => counter.TakeNext("user", Now));
    }
    [Fact]
    public void UncertaintyHoldsTheNumberUntilAuthoritativeNegativeEvidence()
    {
        var r = Reservation(); r.Block("worker", Now, "Timeout"); Assert.True(r.HoldsNumber); Assert.Equal(InvoiceNumberReservationState.Blocked, r.State);
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(Evidence(r, false) with { Kind = InvoiceIssuanceOutcomeKind.Uncertain }, false, "worker", Now));
        r.Resolve(Evidence(r, false), false, "worker", Now.AddMinutes(1));
        Assert.Equal(InvoiceNumberReservationState.Released, r.State); Assert.False(r.HoldsNumber); Assert.Equal("Timeout", r.UncertaintyReason);
        r.Resolve(Evidence(r, false), false, "worker", Now.AddMinutes(2)); Assert.Equal(Now.AddMinutes(1), r.ResolvedAt);
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(Evidence(r, true), true, "worker", Now));
    }
    [Fact]
    public void FinalizationIsIdempotentOnlyForTheSameEvidenceAndExactNumber()
    {
        var r = Reservation(); var evidence = Evidence(r, true); r.Resolve(evidence, true, "worker", Now);
        r.Resolve(evidence, true, "worker", Now.AddMinutes(1));
        Assert.True(r.HoldsNumber); Assert.Equal(InvoiceNumberReservationState.Finalized, r.State); Assert.Equal(evidence.KsefNumber, r.KsefNumber);
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(evidence with { EvidenceId = "OTHER" }, true, "worker", Now));
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(evidence with { KsefNumber = "OTHER" }, true, "worker", Now));
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(Evidence(r, false), false, "worker", Now));
    }
    [Theory]
    [InlineData("tenant")][InlineData("invoice")][InlineData("operation")][InlineData("approval")][InlineData("reservation")][InlineData("number")]
    public void EvidenceCannotBeDetachedFromItsReservationAndInvoice(string changed)
    {
        var r = Reservation(); var e = Evidence(r, true);
        e = changed switch
        {
            "tenant" => e with { TenantId = Guid.NewGuid() }, "invoice" => e with { InvoiceId = Guid.NewGuid() },
            "operation" => e with { OperationId = Guid.NewGuid() }, "approval" => e with { ApprovalId = Guid.NewGuid() },
            "reservation" => e with { ReservationId = Guid.NewGuid() }, _ => e with { InvoiceNumber = "OTHER" }
        };
        Assert.Throws<InvoiceNumberingConflictException>(() => r.Resolve(e, true, "worker", Now));
        Assert.Equal(InvoiceNumberReservationState.Active, r.State); Assert.Null(r.EvidenceId);
    }
}
