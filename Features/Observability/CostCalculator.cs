namespace TokenScope.Features.Observability;

public static class CostCalculator
{
    private static readonly List<(string Prefix, decimal InputPer1M, decimal OutputPer1M)> Pricing =
    [
        ("gpt-4o-mini",         0.15m,    0.60m),
        ("gpt-4o",              2.50m,   10.00m),
        ("gpt-4-turbo",        10.00m,   30.00m),
        ("gpt-3.5-turbo",       0.50m,    1.50m),
        ("claude-3-5-sonnet",   3.00m,   15.00m),
        ("claude-3-5-haiku",    0.80m,    4.00m),
        ("claude-3-opus",      15.00m,   75.00m),
        ("llama-3.3-70b",       0.59m,    0.79m),
        ("llama-3.1-8b-instant",0.05m,    0.08m),
        ("mixtral-8x7b",        0.24m,    0.24m),
        ("gemma2-9b-it",        0.20m,    0.20m),
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
