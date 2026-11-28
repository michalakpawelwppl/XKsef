using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Tests.Domain;

public sealed class SalesInvoiceBankAccountTests
{
    private const string Fa3Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";
    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);

    [Fact]
    public void OfficialSchemaDefinesTwoIndependentOptionalAccountCollections()
    {
        var schema = Schemas.Value.GlobalElements[new XmlQualifiedName("Faktura", Fa3Namespace)]!;
        var accounts = new List<XmlSchemaElement>();
        void Visit(XmlSchemaParticle? particle)
        {
            if (particle is XmlSchemaElement element)
            {
                if (element.Name is "RachunekBankowy" or "RachunekBankowyFaktora") accounts.Add(element);
                if (element.ElementSchemaType is XmlSchemaComplexType type) Visit(type.ContentTypeParticle);
            }
            else if (particle is XmlSchemaGroupBase group)
                foreach (XmlSchemaParticle child in group.Items) Visit(child);
        }
        Visit((XmlSchemaElement)schema);
        var seller = Assert.Single(accounts, x => x.Name == "RachunekBankowy");
        var factor = Assert.Single(accounts, x => x.Name == "RachunekBankowyFaktora");
        Assert.Equal(0m, seller.MinOccurs); Assert.Equal(100m, seller.MaxOccurs);
        Assert.Equal(0m, factor.MinOccurs); Assert.Equal(20m, factor.MaxOccurs);
        Assert.Same(seller.Parent, factor.Parent); Assert.IsType<XmlSchemaSequence>(seller.Parent);
        Assert.Equal(seller.SchemaTypeName, factor.SchemaTypeName);
    }

    public static IEnumerable<object?[]> Accounts()
    {
        yield return [new InvoiceBankAccount("1234567890"), true];
        yield return [new InvoiceBankAccount(new string('X', 34)), true];
        yield return [new InvoiceBankAccount("123456789"), false];
        yield return [new InvoiceBankAccount(new string('X', 35)), false];
        yield return [new InvoiceBankAccount("account/123"), true]; // TNrRB has no digits/IBAN/checksum restriction.
        foreach (var type in new int?[] { null, 1, 2, 3, 0, 4, -1 })
            yield return [new InvoiceBankAccount("1234567890", BankOwnAccountType: (BankOwnAccountType?)type), type is null or >= 1 and <= 3];
        foreach (var swift in new[] { "WBKPPLPP", "ABCDEFGH123", "ABCDEF12", "abcdef12", "ABCDE123", "ABCDEFGH1", "ABCDEFGH1234", "", "ABCDEFGH\n" })
            yield return [new InvoiceBankAccount("1234567890", swift), swift is "WBKPPLPP" or "ABCDEFGH123" or "ABCDEF12" or "ABCDEFGH\n"];
        yield return [new InvoiceBankAccount("1234567890", BankName: new string('B', 256), Description: "Opis"), true];
        yield return [new InvoiceBankAccount("1234567890", BankName: new string('B', 257)), false];
        yield return [new InvoiceBankAccount("1234567890", Description: new string('B', 257)), false];
        yield return [new InvoiceBankAccount("1234567890", BankName: ""), false];
        yield return [new InvoiceBankAccount("1234567890", Description: " \t\n"), false];
        yield return [new InvoiceBankAccount("1234567890", BankName: new string(' ', 300) + "Bank\t nazwa  "), true];
    }

    [Theory]
    [MemberData(nameof(Accounts))]
    public void BothAccountKindsMatchOfficialFa3Xsd(InvoiceBankAccount account, bool valid)
    {
        Assert.Equal(valid, IsValidAgainstXsd(account));
        foreach (var factor in new[] { false, true })
        {
            var draft = new SalesInvoiceDraft { Details = factor ? new() { FactorBankAccounts = [account] } : new() { BankAccounts = [account] } };
            if (valid) SalesInvoiceValidator.Validate(draft);
            else
            {
                var error = Assert.Throws<SalesInvoiceValidationException>(() => SalesInvoiceValidator.Validate(draft));
                Assert.All(error.Issues, x => Assert.StartsWith(factor ? "details.factorBankAccounts[0]" : "details.bankAccounts[0]", x.Path));
            }
        }
    }

    [Theory]
    [InlineData(0, 0, true)] [InlineData(0, 20, true)] [InlineData(100, 0, true)]
    [InlineData(100, 20, true)] [InlineData(101, 0, false)] [InlineData(0, 21, false)]
    public void CollectionsAreOptionalIndependentAndHaveSeparateLimits(int sellers, int factors, bool valid)
    {
        var account = new InvoiceBankAccount("1234567890");
        var draft = new SalesInvoiceDraft { Details = new() { BankAccounts = Enumerable.Repeat(account, sellers).ToArray(), FactorBankAccounts = Enumerable.Repeat(account, factors).ToArray() } };
        if (valid) SalesInvoiceValidator.Validate(draft);
        else Assert.Throws<SalesInvoiceValidationException>(() => SalesInvoiceValidator.Validate(draft));
    }

    [Fact]
    public void InvalidXmlAndNullEntriesAreReportedWithIndexedPaths()
    {
        var error = Assert.Throws<SalesInvoiceValidationException>(() => SalesInvoiceValidator.Validate(new() { Details = new()
        {
            BankAccounts = [null!], FactorBankAccounts = [new("1234567890", Description: "Opis\u0001")]
        } }));
        Assert.Contains(error.Issues, x => x.Path == "details.bankAccounts[0]" && x.Code == "BankAccountRequired");
        Assert.Contains(error.Issues, x => x.Path == "details.factorBankAccounts[0].description" && x.Code == "InvalidXmlCharacters");
    }

    [Fact]
    public void AggregateCopiesBothCollectionsAndDoesNotNormalizeUsersData()
    {
        var seller = new InvoiceBankAccount("1234567890", BankOwnAccountType: BankOwnAccountType.PurchasedReceivables);
        var factor = new InvoiceBankAccount("factor/123", BankName: " Bank\t nazwa ", BankOwnAccountType: BankOwnAccountType.CollectionsForSupplier);
        var sellers = new[] { seller }; var factors = new[] { factor };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new() { Details = new() { BankAccounts = sellers, FactorBankAccounts = factors } }, "actor", DateTimeOffset.UtcNow, new());
        sellers[0] = new("9876543210"); factors[0] = new("9876543210");
        Assert.Equal(seller, Assert.Single(invoice.Details.Data.BankAccounts));
        Assert.Equal(factor, Assert.Single(invoice.Details.Data.FactorBankAccounts));
        var copy = invoice.ToDraft(); ((InvoiceBankAccount[])copy.Details.FactorBankAccounts)[0] = new("9876543210");
        Assert.Equal(factor, Assert.Single(invoice.ToDraft().Details.FactorBankAccounts));
        Assert.Throws<NotSupportedException>(() => ((IList<InvoiceBankAccount>)invoice.Details.Data.FactorBankAccounts).Clear());
    }

    private static bool IsValidAgainstXsd(InvoiceBankAccount account)
    {
        XNamespace ns = Fa3Namespace;
        var root = new XElement(ns + "BankAccountTest", new XElement(ns + "NrRB", account.Number));
        if (account.Swift is not null) root.Add(new XElement(ns + "SWIFT", account.Swift));
        if (account.BankOwnAccountType is { } type) root.Add(new XElement(ns + "RachunekWlasnyBanku", (int)type));
        if (account.BankName is not null) root.Add(new XElement(ns + "NazwaBanku", account.BankName));
        if (account.Description is not null) root.Add(new XElement(ns + "OpisRachunku", account.Description));
        var valid = true;
        new XDocument(root).Validate(Schemas.Value, (_, _) => valid = false);
        return valid;
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XKsef.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var set = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { "schemat.xsd", "StrukturyDanych_v10-0E.xsd", "ElementarneTypyDanych_v10-0E.xsd", "KodyKrajow_v10-0E.xsd" })
        {
            using var reader = XmlReader.Create(Path.Combine(directory.FullName, "docs", "analysis", "fa3-sources", file), new() { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });
            var schema = XmlSchema.Read(reader, null)!;
            foreach (var include in schema.Includes.OfType<XmlSchemaInclude>().ToArray()) schema.Includes.Remove(include);
            foreach (XmlSchemaExternal external in schema.Includes) external.SchemaLocation = null;
            if (file == "schemat.xsd") schema.Items.Add(new XmlSchemaElement { Name = "BankAccountTest", SchemaTypeName = new("TRachunekBankowy", Fa3Namespace) });
            set.Add(schema);
        }
        set.Compile(); return set;
    }
}
