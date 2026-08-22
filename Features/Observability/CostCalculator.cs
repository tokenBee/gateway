namespace TokenBee.Features.Observability;

public static class CostCalculator
{
    // Longer prefixes must come first (prefix match is StartsWith).
    // Rates are USD per 1M tokens. Sources: provider public pricing as of Aug 2026.
    private static readonly List<(string Prefix, decimal InputPer1M, decimal OutputPer1M)> Pricing =
    [
        // OpenAI — GPT-5.6 / GPT-5 family (longer prefixes first)
        ("gpt-5.6-sol",          4.00m,   20.00m),
        ("gpt-5.6-terra",        2.00m,   12.00m),
        ("gpt-5.6-luna",         0.20m,    1.20m),
        ("gpt-5.4-mini",         0.75m,    4.50m),
        ("gpt-5.4-nano",         0.20m,    1.25m),
        ("gpt-5.4",              2.50m,   15.00m),
        ("gpt-5-mini",           0.25m,    2.00m),
        ("gpt-5-nano",           0.05m,    0.40m),
        ("gpt-5-pro",           15.00m,   120.00m),
        ("gpt-5",                1.25m,   10.00m),
        ("gpt-4.5",             15.00m,   75.00m),
        ("gpt-4.1-mini",          0.40m,    1.60m),
        ("gpt-4.1-nano",          0.10m,    0.40m),
        ("gpt-4.1",               2.00m,    8.00m),
        ("gpt-4o-mini",          0.15m,    0.60m),
        ("gpt-4o",               2.50m,   10.00m),
        ("o4-mini",              1.10m,    4.40m),
        ("o3-mini",              1.10m,    4.40m),
        ("o3",                  10.00m,   40.00m),
        ("o1-mini",              3.00m,   12.00m),
        ("o1",                  15.00m,   60.00m),

        // Anthropic
        ("claude-opus-4",       15.00m,   75.00m),
        ("claude-sonnet-4",      3.00m,   15.00m),
        ("claude-haiku-4",       0.80m,    4.00m),
        ("claude-4-opus",       15.00m,   75.00m),
        ("claude-4-sonnet",      3.00m,   15.00m),
        ("claude-4-haiku",       0.80m,    4.00m),
        ("claude-3-7-sonnet",    3.00m,   15.00m),
        ("claude-3-5-sonnet",    3.00m,   15.00m),
        ("claude-3-5-haiku",     0.80m,    4.00m),
        ("claude-3-opus",       15.00m,   75.00m),

        // Google
        ("gemini-2.5-pro",       1.25m,   10.00m),
        ("gemini-2.5-flash",     0.15m,    0.60m),
        ("gemini-2.0-flash",     0.10m,    0.40m),
        ("gemini-1.5-pro",       1.25m,    5.00m),

        // Mistral
        ("mistral-large",        2.00m,    6.00m),
        ("mistral-small",        0.20m,    0.60m),
        ("open-mistral-nemo",    0.15m,    0.15m),
        ("pixtral-large",        2.00m,    6.00m),

        // Perplexity
        ("sonar-pro",            3.00m,   15.00m),
        ("sonar-reasoning",      5.00m,   25.00m),
        ("sonar",                1.00m,    5.00m),

        // Groq (current production / preview — Aug 2026)
        ("gpt-oss-120b",         0.15m,    0.60m),
        ("gpt-oss-20b",          0.075m,   0.30m),
        ("gpt-oss-safeguard-20b",0.075m,   0.30m),
        ("qwen3.6-27b",          0.60m,    3.00m),
        ("qwen3-32b",            0.29m,    0.59m),

        // Groq legacy (kept for historical traces after deprecation)
        ("llama-3.3-70b",        0.59m,    0.79m),
        ("llama-3.1-8b",         0.05m,    0.08m),
        ("deepseek-r1-distill",  0.55m,    2.19m),

        // xAI
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
        // Strip provider prefix if present (e.g. "openai/gpt-oss-120b", "qwen/qwen3.6-27b")
        var normalized = model;
        var slash = model.LastIndexOf('/');
        if (slash >= 0 && slash < model.Length - 1)
            normalized = model[(slash + 1)..];

        foreach (var (prefix, inputPer1M, outputPer1M) in Pricing)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                model.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                return (inputPer1M, outputPer1M);
        }

        return (DefaultInputPer1M, DefaultOutputPer1M);
    }
}
