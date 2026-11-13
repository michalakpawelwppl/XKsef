using System.Globalization;
using System.Text;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Invoices;

namespace xKsef.Tests;

public sealed class KsefInvoiceMetadataParserTests
{
    private const string Fa2 = "http://crd.gov.pl/wzor/2023/06/29/12648/";
    private const string Fa3 = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    [Theory]
    [InlineData(Fa2)]
    [InlineData(Fa3)]
    public void ReadsExplicitHeaderTotalsAndBuyerWithoutChangingGross(string ns)
    {
        InvoiceMetadata result = Parse("""
            <P_13_1>100.00</P_13_1><P_14_1>23.00</P_14_1>
            <P_13_2>200.00</P_13_2><P_14_2>16.00</P_14_2>
            """, ns, buyer: "<NIP>9876543210</NIP><Nazwa>Firma &amp; Syn</Nazwa>");

        Assert.Equal(300m, result.NetAmount);
        Assert.Equal(39m, result.VatAmount);
        Assert.Equal("9876543210", result.BuyerNip);
        Assert.Equal("Firma & Syn", result.BuyerName);
        Assert.Equal(9999.99m, result.GrossAmount);
        Assert.Equal("FV/1", result.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 11, 13), result.IssueDate);
        Assert.Equal("Seller", result.SellerName);
        Assert.Equal("1234567890", result.SellerNip);
        Assert.Equal("PLN", result.Currency);
    }

    [Theory]
    [InlineData(Fa2)]
    [InlineData(Fa3)]
    public void SumsAllSupportedDocumentSummaryCategories(string ns)
    {
        InvoiceMetadata result = Parse("""
            <P_13_1>100</P_13_1><P_14_1>23</P_14_1>
            <P_13_2>200</P_13_2><P_14_2>16</P_14_2>
            <P_13_3>300</P_13_3><P_14_3>15</P_14_3>
            <P_13_4>400</P_13_4><P_14_4>16</P_14_4>
            <P_13_5>500</P_13_5><P_14_5>95</P_14_5>
            <P_13_6_1>10</P_13_6_1><P_13_6_2>20</P_13_6_2><P_13_6_3>30</P_13_6_3>
            <P_13_7>40</P_13_7><P_13_8>50</P_13_8><P_13_9>60</P_13_9><P_13_10>70</P_13_10>
            """, ns);
        Assert.Equal(1780m, result.NetAmount);
        Assert.Equal(165m, result.VatAmount);
    }

    [Theory]
    [InlineData("P_13_6_1")]
    [InlineData("P_13_6_2")]
    [InlineData("P_13_6_3")]
    [InlineData("P_13_7")]
    [InlineData("P_13_8")]
    [InlineData("P_13_9")]
    [InlineData("P_13_10")]
    public void ExplicitZeroRatedOrNonTaxableSummaryJustifiesZeroVat(string field)
    {
        InvoiceMetadata result = Parse($"<{field}>100.00</{field}>");
        Assert.Equal(100m, result.NetAmount);
        Assert.Equal(0m, result.VatAmount);
    }

    [Theory]
    [InlineData("KOR", "-100.00", "-23.00")]
    [InlineData("KOR", "100.00", "23.00")]
    [InlineData("KOR", "0.00", "0.00")]
    [InlineData("ZAL", "100.00", "23.00")]
    [InlineData("KOR_ZAL", "-100.00", "-23.00")]
    [InlineData("ROZ", "100.00", "23.00")]
    [InlineData("KOR_ROZ", "-100.00", "-23.00")]
    public void KeepsDocumentSummarySignsForCorrectionsAndAdvanceInvoices(string kind, string net, string vat)
    {
        InvoiceMetadata result = Parse($"<P_13_1>{net}</P_13_1><P_14_1>{vat}</P_14_1>", kind: kind);
        Assert.Equal(decimal.Parse(net, CultureInfo.InvariantCulture), result.NetAmount);
        Assert.Equal(decimal.Parse(vat, CultureInfo.InvariantCulture), result.VatAmount);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("PLN")]
    public void VatUsesInvoiceCurrencySummaryAndIgnoresPlnAndLineAmounts(string currency)
    {
        InvoiceMetadata result = Parse("""
            <P_13_1>100</P_13_1><P_14_1>23</P_14_1><P_14_1W>99.99</P_14_1W>
            <P_13_2>200</P_13_2><P_14_2>16</P_14_2><P_14_2W>77.77</P_14_2W>
            <P_14_3W>999</P_14_3W><P_14_4W>999</P_14_4W>
            <Zamowienie><P_13_1>999999</P_13_1></Zamowienie>
            <FaWiersz><P_11>5000</P_11><StanPrzed>1</StanPrzed><P_14_1>500</P_14_1></FaWiersz>
            <FaWiersz><P_11>5100</P_11><P_14_1>510</P_14_1></FaWiersz>
            """, currency: currency);
        Assert.Equal(300m, result.NetAmount);
        Assert.Equal(39m, result.VatAmount);
        Assert.Equal(currency, result.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<FaWiersz><P_11>100</P_11><P_12>23</P_12></FaWiersz>")]
    [InlineData("<P_14_1W>23</P_14_1W>")]
    [InlineData("<extension:P_13_1 xmlns:extension='urn:extension'>100</extension:P_13_1>")]
    public void MissingHeaderTotalsRemainUnknownInsteadOfBeingDerivedFromGrossOrRows(string summary)
    {
        InvoiceMetadata result = Parse(summary);
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    [Fact]
    public void MissingVatForTaxableBaseDoesNotBecomeZero()
    {
        InvoiceMetadata result = Parse("<P_13_1>100</P_13_1><P_13_7>50</P_13_7>");
        Assert.Equal(150m, result.NetAmount);
        Assert.Null(result.VatAmount);
    }

    [Theory]
    [InlineData("<P_14_1>23</P_14_1>")]
    [InlineData("<P_13_7>50</P_13_7><P_14_1>23</P_14_1>")]
    public void OrphanVatDoesNotProducePartialNetOrTaxTotal(string summary)
    {
        InvoiceMetadata result = Parse(summary);
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
    }

    [Theory]
    [InlineData("UPR")]
    [InlineData("UNKNOWN")]
    [InlineData("vat")]
    [InlineData("")]
    public void UnsupportedOrMissingDocumentKindLeavesNewAmountsUnknown(string kind)
    {
        InvoiceMetadata result = Parse("<P_13_1>100</P_13_1><P_14_1>23</P_14_1>", kind: kind);
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("urn:future-schema")]
    [InlineData("http://crd.gov.pl/wzor/2021/11/29/11089/")]
    public void UnsupportedNamespacePreservesLegacyFieldsButDoesNotGuessAmounts(string ns)
    {
        InvoiceMetadata result = Parse("<P_13_1>100</P_13_1><P_14_1>23</P_14_1>", ns);
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
        Assert.Equal("FV/1", result.InvoiceNumber);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    [Theory]
    [InlineData("<P_13_11>100</P_13_11>")]
    [InlineData("<P_13_11>0</P_13_11>")]
    [InlineData("<Adnotacje><PMarzy><P_PMarzy>1</P_PMarzy></PMarzy></Adnotacje>")]
    [InlineData("<P_13_99>100</P_13_99>")]
    [InlineData("<P_14_99>23</P_14_99>")]
    [InlineData("<RodzajFaktury>KOR</RodzajFaktury>")]
    public void MarginUnknownSummaryFieldsAndAmbiguousKindDoNotProducePartialAmounts(string extra)
    {
        InvoiceMetadata result = Parse("<P_13_1>100</P_13_1><P_14_1>23</P_14_1>" + extra);
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
    }

    [Theory]
    [InlineData("1,23")]
    [InlineData("1e2")]
    [InlineData("1 000.00")]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("0.001")]
    [InlineData("999999999999999999999999999999999999")]
    public void InvalidOrUnrepresentableNetRemainsNullWithoutBlockingLegacyImport(string amount)
    {
        InvoiceMetadata result = Parse($"<P_13_1>{amount}</P_13_1><P_14_1>23</P_14_1>");
        Assert.Null(result.NetAmount);
        Assert.Equal(23m, result.VatAmount);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("23,00")]
    [InlineData("23.001")]
    [InlineData("")]
    public void InvalidVatRemainsNullWhileExplicitNetIsPreserved(string amount)
    {
        InvoiceMetadata result = Parse($"<P_13_1>100</P_13_1><P_14_1>{amount}</P_14_1>");
        Assert.Equal(100m, result.NetAmount);
        Assert.Null(result.VatAmount);
    }

    [Fact]
    public void DuplicateSummaryIsNotSummedTwice()
    {
        InvoiceMetadata netDuplicate = Parse("<P_13_1>100</P_13_1><P_13_1>200</P_13_1><P_14_1>23</P_14_1>");
        Assert.Null(netDuplicate.NetAmount);
        InvoiceMetadata vatDuplicate = Parse("<P_13_1>100</P_13_1><P_14_1>23</P_14_1><P_14_1>46</P_14_1>");
        Assert.Equal(100m, vatDuplicate.NetAmount);
        Assert.Null(vatDuplicate.VatAmount);
    }

    [Fact]
    public void OverflowDoesNotRoundOrThrowFromNewAmountParsing()
    {
        InvoiceMetadata result = Parse("""
            <P_13_1>79228162514264337593543950335</P_13_1><P_14_1>0</P_14_1>
            <P_13_2>1</P_13_2><P_14_2>0</P_14_2>
            """);
        Assert.Null(result.NetAmount);
        Assert.Equal(0m, result.VatAmount);
    }

    [Fact]
    public void ReadsDecimalsIndependentlyOfProcessCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            InvoiceMetadata result = Parse("<P_13_1>-100.25</P_13_1><P_14_1>-23.06</P_14_1>");
            Assert.Equal(-100.25m, result.NetAmount);
            Assert.Equal(-23.06m, result.VatAmount);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("<KodUE>DE</KodUE><NrVatUE>123456789</NrVatUE><Nazwa>Buyer EU</Nazwa>", null, "Buyer EU")]
    [InlineData("<KodKraju>US</KodKraju><NrID>ABC</NrID><Nazwa>Buyer US</Nazwa>", null, "Buyer US")]
    [InlineData("<BrakID>1</BrakID><Nazwa>Consumer</Nazwa>", null, "Consumer")]
    [InlineData("<NIP> 9876543210 </NIP><Nazwa> Buyer </Nazwa>", "9876543210", "Buyer")]
    [InlineData("<NIP>invalid</NIP><Nazwa>Buyer</Nazwa>", null, "Buyer")]
    [InlineData("<NIP>９８７６５４３２１０</NIP><Nazwa>Buyer</Nazwa>", null, "Buyer")]
    [InlineData("<NIP>9876543210</NIP>", "9876543210", null)]
    [InlineData("<NIP>9876543210</NIP><NIP>1234567890</NIP><Nazwa>Buyer</Nazwa>", null, "Buyer")]
    [InlineData("<Nazwa> </Nazwa>", null, null)]
    public void OptionalBuyerFieldsDoNotTreatForeignIdentifiersAsNip(string buyer, string? expectedNip, string? expectedName)
    {
        InvoiceMetadata result = Parse("", buyer: buyer);
        Assert.Equal(expectedNip, result.BuyerNip);
        Assert.Equal(expectedName, result.BuyerName);
    }

    [Fact]
    public void MissingOrAmbiguousBuyerDoesNotBlockLegacyFields()
    {
        string xml = Xml("");
        InvoiceMetadata missing = new KsefInvoiceMetadataParser().Parse(Encoding.UTF8.GetBytes(xml.Replace("<Podmiot2><DaneIdentyfikacyjne></DaneIdentyfikacyjne></Podmiot2>", "")));
        Assert.Null(missing.BuyerNip);
        Assert.Null(missing.BuyerName);
        InvoiceMetadata ambiguous = new KsefInvoiceMetadataParser().Parse(Encoding.UTF8.GetBytes(xml.Replace("</Podmiot2>", "</Podmiot2><Podmiot2><DaneIdentyfikacyjne><Nazwa>Other</Nazwa></DaneIdentyfikacyjne></Podmiot2>")));
        Assert.Null(ambiguous.BuyerName);
        Assert.Equal("FV/1", ambiguous.InvoiceNumber);
    }

    [Theory]
    [InlineData("gross-missing")]
    [InlineData("gross-invalid")]
    [InlineData("date-invalid")]
    [InlineData("currency-missing")]
    public void ExistingValidationFailuresRemainUnchanged(string failure)
    {
        string xml = Xml("<P_13_1>100</P_13_1><P_14_1>23</P_14_1>");
        xml = failure switch
        {
            "gross-missing" => xml.Replace("<P_15>9999.99</P_15>", ""),
            "gross-invalid" => xml.Replace("<P_15>9999.99</P_15>", "<P_15>invalid</P_15>"),
            "date-invalid" => xml.Replace("2026-11-13", "invalid-date"),
            _ => xml.Replace("<KodWaluty>PLN</KodWaluty>", "")
        };
        Assert.Throws<FormatException>(() => new KsefInvoiceMetadataParser().Parse(Encoding.UTF8.GetBytes(xml)));
    }

    [Fact]
    public async Task ExistingImporterAndFactoryRemainCompatibleWithExtendedMetadata()
    {
        Guid tenantId = Guid.NewGuid();
        byte[] xml = Encoding.UTF8.GetBytes(Xml("<P_13_1>100</P_13_1><P_14_1>invalid</P_14_1>", buyer: "<Nazwa>Buyer</Nazwa>"));
        var repository = new RecordingRepository();
        var importer = new InvoiceImportService(repository, new KsefInvoiceMetadataParser());
        var invoice = new ValidatedInvoice("ksef-number", xml);
        await importer.ImportAsync(tenantId, invoice, InvoiceDirection.Purchase);
        StoredInvoice saved = Assert.IsType<StoredInvoice>(repository.Saved);
        Assert.Equal(tenantId, saved.TenantId);
        Assert.Equal("ksef-number", saved.KsefNumber);
        Assert.Equal("FV/1", saved.InvoiceNumber);
        Assert.Equal(9999.99m, saved.GrossAmount);
        Assert.Equal("PLN", saved.Currency);
        Assert.Equal("Seller", saved.SellerName);
        Assert.Equal("1234567890", saved.SellerNip);
        Assert.Equal(InvoiceDirection.Purchase, saved.Direction);
        Assert.Equal(xml, saved.XmlBytes);
        await Assert.ThrowsAsync<DuplicateInvoiceException>(() => importer.ImportAsync(tenantId, invoice, InvoiceDirection.Purchase));
        Assert.Equal(1, repository.SaveCalls);
    }

    [Theory]
    [InlineData("duplicate-fa")]
    [InlineData("nested-kind")]
    [InlineData("unknown-root")]
    public void AmbiguousDocumentStructureDoesNotProduceNewTotals(string variant)
    {
        string xml = Xml("<P_13_1>100</P_13_1><P_14_1>23</P_14_1>");
        xml = variant switch
        {
            "duplicate-fa" => xml.Replace("</Faktura>", "<Fa><P_13_1>500</P_13_1></Fa></Faktura>"),
            "nested-kind" => xml.Replace("<RodzajFaktury>VAT</RodzajFaktury>", "<RodzajFaktury><Value>VAT</Value></RodzajFaktury>"),
            _ => xml.Replace("Faktura", "UnknownDocument")
        };
        InvoiceMetadata result = new KsefInvoiceMetadataParser().Parse(Encoding.UTF8.GetBytes(xml));
        Assert.Null(result.NetAmount);
        Assert.Null(result.VatAmount);
        Assert.Equal(9999.99m, result.GrossAmount);
    }

    private static InvoiceMetadata Parse(string summary, string ns = Fa3, string kind = "VAT", string buyer = "", string currency = "PLN")
        => new KsefInvoiceMetadataParser().Parse(Encoding.UTF8.GetBytes(Xml(summary, ns, kind, buyer, currency)));

    private static string Xml(string summary, string ns = Fa3, string kind = "VAT", string buyer = "", string currency = "PLN") => $$"""
        <Faktura xmlns="{{ns}}">
            <Podmiot1><DaneIdentyfikacyjne><NIP>1234567890</NIP><Nazwa>Seller</Nazwa></DaneIdentyfikacyjne></Podmiot1>
            <Podmiot2><DaneIdentyfikacyjne>{{buyer}}</DaneIdentyfikacyjne></Podmiot2>
            <Fa>
                <KodWaluty>{{currency}}</KodWaluty><P_1>2026-11-13</P_1><P_2>FV/1</P_2>
                {{summary}}
                <P_15>9999.99</P_15>
                {{(kind.Length == 0 ? "" : "<RodzajFaktury>" + kind + "</RodzajFaktury>")}}
            </Fa>
        </Faktura>
        """;

    private sealed class RecordingRepository : IInvoiceRepository
    {
        public StoredInvoice? Saved { get; private set; }
        public int SaveCalls { get; private set; }
        public Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default)
        {
            Saved = invoice;
            SaveCalls++;
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Saved?.TenantId == tenantId && Saved.KsefNumber == ksefNumber);
        public Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
