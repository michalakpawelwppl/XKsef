using Microsoft.Extensions.Options;

namespace xKsef.Infrastructure.Gus;

public enum GusBirEnvironment
{
    Test,
    Production
}

public sealed class GusBirOptions
{
    public const string SectionName = "GusBir";
    public const string TestServiceUrl = "https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc";
    public const string ProductionServiceUrl = "https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc";
    public const string PublicTestApiKey = "abcde12345abcde12345";

    public GusBirEnvironment Environment { get; set; } = GusBirEnvironment.Test;
    public string? ServiceUrl { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    public Uri Endpoint => new(ServiceUrl ?? (Environment == GusBirEnvironment.Production ? ProductionServiceUrl : TestServiceUrl));
    public string EffectiveApiKey => ApiKey ?? (Environment == GusBirEnvironment.Test ? PublicTestApiKey : string.Empty);
}

public sealed class GusBirOptionsValidator : IValidateOptions<GusBirOptions>
{
    public ValidateOptionsResult Validate(string? name, GusBirOptions options)
    {
        var failures = new List<string>();
        if (!Enum.IsDefined(options.Environment))
            failures.Add("GusBir:Environment musi mieć wartość Test albo Production.");
        if (options.TimeoutSeconds is < 1 or > 300)
            failures.Add("GusBir:TimeoutSeconds musi mieścić się w zakresie 1–300 sekund.");
        var expected = options.Environment == GusBirEnvironment.Production ? GusBirOptions.ProductionServiceUrl : GusBirOptions.TestServiceUrl;
        if (!Uri.TryCreate(options.ServiceUrl ?? expected, UriKind.Absolute, out var endpoint) || endpoint.AbsoluteUri != expected)
            failures.Add("GusBir:ServiceUrl musi wskazywać oficjalny adres wybranego środowiska GUS.");
        var key = options.EffectiveApiKey;
        if (key.Length != 20 || key.Any(character => !char.IsAsciiLetterOrDigit(character)))
            failures.Add("GusBir:ApiKey musi zawierać 20 liter lub cyfr ASCII.");
        if (options.Environment == GusBirEnvironment.Production && key == GusBirOptions.PublicTestApiKey)
            failures.Add("Publiczny klucz testowy GUS nie może być użyty w środowisku Production.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
