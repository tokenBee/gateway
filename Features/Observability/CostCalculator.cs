namespace TokenBee.Features.Observability;

public static class CostCalculator
{
    private static readonly List<(string Prefix, decimal InputPer1M, decimal OutputPer1M)> Pricing =
    [
        ("gpt-4.5",              15.00m,  75.00m),
        ("gpt-4o-mini",          0.15m,    0.60m),
        ("gpt-4o",               2.50m,   10.00m),
        ("o1-mini",              3.00m,   12.00m),
        ("o1",                  15.00m,   60.00m),
        ("o3-mini",              1.10m,    4.40m),
        ("claude-3-7-sonnet",    3.00m,   15.00m),
        ("claude-3-5-sonnet",    3.00m,   15.00m),
        ("claude-3-5-haiku",     0.80m,    4.00m),
        ("claude-3-opus",       15.00m,   75.00m),
        ("gemini-3.1-pro",       1.25m,    5.00m),
        ("gemini-3.1-flash",     0.075m,   0.30m),
        ("gemini-2.5-pro",       1.25m,    5.00m),
        ("gemini-2.0-flash",     0.075m,   0.30m),
        ("gemini-1.5-pro",       1.25m,    5.00m),
        ("mistral-large",        2.00m,    6.00m),
        ("mistral-small",        0.20m,    0.60m),
        ("open-mistral-nemo",    0.15m,    0.15m),
        ("pixtral-large",        2.00m,    6.00m),
        ("sonar-pro",            3.00m,   15.00m),
        ("sonar-reasoning",      5.00m,   25.00m),
        ("sonar",                1.00m,    5.00m),
        ("llama-3.3-70b",        0.59m,    0.79m),
        ("llama-3.1-8b",         0.05m,    0.08m),
        ("deepseek-r1-distill",  0.55m,    2.19m),
        ("grok-3",               5.00m,   15.00m),
        ("grok-2-mini",          0.60m,    2.40m),
        ("grok-2",               2.00m,   10.00m),
    ];

    private const decimal DefaultInputPer1M  = 0.15m;
    private const decimal DefaultOutputPer1M = 0.60m;

    public static (decimal InputCost, decimal OutputCost, decimal TotalCost) Calculate(
        string model, int inputTokens, int outputTokens)
    {
        var (inputRate, outputRate) = GetRates(model);

        var inputCost  = Math.Round(inputTokens  / 1_000_000m * inputRate,  8);
        var outputCost = Math.Round(outputTokens / 1_000_000m * outputRate, 8);
        var totalCost  = Math.Round(inputCost + outputCost, 8);

        return (inputCost, outputCost, totalCost);
    }

    private static (decimal InputRate, decimal OutputRate) GetRates(string model)
    {
        foreach (var (prefix, inputPer1M, outputPer1M) in Pricing)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (inputPer1M, outputPer1M);
        }

        return (DefaultInputPer1M, DefaultOutputPer1M);
    }
}
