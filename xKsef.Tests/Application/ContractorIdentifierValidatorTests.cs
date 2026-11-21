using xKsef.Application.Contractors;

namespace xKsef.Tests.Application;

public sealed class ContractorIdentifierValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    public void MissingOptionalIdentifiersNormalizeToNull(string? value)
    {
        Assert.Null(ContractorIdentifierNormalizer.NormalizeOptional(value));
        Assert.Null(ContractorIdentifierValidator.NormalizeAndValidateNip(value));
        Assert.Null(ContractorIdentifierValidator.NormalizeAndValidateRegon(value));
        Assert.Null(ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(value));
        Assert.Null(ContractorIdentifierValidator.NormalizeAndValidateOtherTaxNumber(value));
        Assert.Null(ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(value, false));
    }

    [Fact]
    public void NormalizationPreservesLeadingZerosCaseAndInternalCharacters()
    {
        Assert.Equal("001-ab CD", ContractorIdentifierNormalizer.NormalizeOptional(" \t001-ab CD\r\n"));
    }

    [Theory]
    [InlineData("8567346215")]
    [InlineData("0123456789")]
    [InlineData("856-734-62-15")]
    [InlineData("856734 6215")]
    public void NipAcceptsValidChecksumAndPreservesOriginalFormatAndLeadingZeros(string value)
    {
        Assert.Equal(value, ContractorIdentifierValidator.NormalizeAndValidateNip(" \t" + value + "\r\n"));
    }

    [Theory]
    [InlineData("8567346216")]
    [InlineData("1234567890")] // Remainder 10 must not be converted to zero for NIP.
    [InlineData("856734621")]
    [InlineData("85673462150")]
    [InlineData("856-734-62-16")]
    [InlineData("856734 6216")]
    [InlineData("PL8567346215")]
    [InlineData("856734621A")]
    [InlineData("٨567346215")]
    [InlineData("８５６７３４６２１５")]
    public void NipRejectsInvalidFormatOrChecksum(string value)
    {
        Assert.Equal("nip", Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateNip(value)).ParamName);
    }

    [Theory]
    [InlineData("192598184")]
    [InlineData("123456785")]
    [InlineData("012345675")]
    [InlineData("000000030")] // Remainder 10 is converted to zero for REGON.
    [InlineData("12345678512347")]
    [InlineData("01234567512342")]
    [InlineData("01234567500030")] // Remainder 10 in the full local unit checksum.
    public void RegonAcceptsValidChecksumsAndPreservesLeadingZeros(string value)
    {
        Assert.Equal(value, ContractorIdentifierValidator.NormalizeAndValidateRegon(" \t" + value + "\r\n"));
    }

    [Theory]
    [InlineData("192598183")]
    [InlineData("12345678512348")] // Invalid local unit checksum.
    [InlineData("12345678612342")] // Valid full checksum, invalid nine-digit parent checksum.
    [InlineData("000000031")]
    [InlineData("01234567500031")]
    [InlineData("12345678")]
    [InlineData("1234567850")]
    [InlineData("1234567851234")]
    [InlineData("123456785123470")]
    [InlineData("123-456785")]
    [InlineData("1234 6785")]
    [InlineData("12345678A")]
    [InlineData("１２３４５６７８５")]
    public void RegonRejectsInvalidFormatOrEitherChecksum(string value)
    {
        Assert.Equal("regon", Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateRegon(value)).ParamName);
    }

    [Theory]
    [InlineData("0000123456")]
    [InlineData("1234567890")]
    public void KrsAcceptsTenDigitsWithoutLosingLeadingZeros(string value)
    {
        Assert.Equal(value, ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(" " + value + " "));
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("00001234567")]
    [InlineData("0000-123456")]
    [InlineData("0000 12345")]
    [InlineData("000012345A")]
    [InlineData("００００１２３４５６")]
    public void KrsRejectsInvalidFormat(string value)
    {
        Assert.Equal("krsNumber", Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(value)).ParamName);
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("", false, null)]
    [InlineData(" \t", false, null)]
    [InlineData("PL8567346215", true, "PL8567346215")]
    [InlineData(" pl001Ab ", true, "pl001Ab")]
    public void EuVatAcceptsConsistentRegistrationAndNormalizesNumber(string? value, bool registered, string? expected)
    {
        Assert.Equal(expected, ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(value, registered));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t\r\n", true)]
    [InlineData("PL8567346215", false)]
    [InlineData(" PL8567346215 ", false)]
    public void EuVatRejectsInconsistentRegistration(string? value, bool registered)
    {
        Assert.Equal("euVatNumber", Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(value, registered)).ParamName);
    }

    [Fact]
    public void EuVatLengthLimitAppliesAfterTrimmingWithoutTruncation()
    {
        var maximum = "PL" + new string('1', 30);
        Assert.Equal(maximum, ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(" " + maximum + " ", true));
        Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(maximum + "1", true));
        Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(maximum + "1", false));
    }

    [Fact]
    public void OtherTaxNumberLengthLimitAppliesAfterTrimmingWithoutChangingContents()
    {
        var maximum = new string('a', 100);
        Assert.Equal(maximum, ContractorIdentifierValidator.NormalizeAndValidateOtherTaxNumber(" " + maximum + " "));
        Assert.Equal("001-ab CD", ContractorIdentifierValidator.NormalizeAndValidateOtherTaxNumber(" 001-ab CD "));
        Assert.Equal("otherTaxNumber", Assert.Throws<ArgumentException>(() => ContractorIdentifierValidator.NormalizeAndValidateOtherTaxNumber(maximum + "a")).ParamName);
    }
}
