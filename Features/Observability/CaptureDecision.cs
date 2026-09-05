namespace TokenBee.Features.Observability;

/// <summary>
/// Resolves whether interaction content should be retained.
/// Precedence: per-request capture header → project setting → default on.
/// Plan limits never block the request; they only skip content retention.
/// </summary>
public static class CaptureDecision
{
    public const int FreeMonthlyLimit = 1_000;
    public const int ProMonthlyLimit = 25_000;
    public const int TeamMonthlyLimit = 100_000;

    public static bool? ParseOverride(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        return header.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "yes" => true,
            "false" or "0" or "off" or "no" => false,
            _ => null
        };
    }

    public static bool ShouldStoreContent(
        string? captureHeader,
        bool projectCaptureEnabled,
        bool captureMessages,
        bool overPlanCaptureLimit)
    {
        var perRequest = ParseOverride(captureHeader);
        if (perRequest == false) return false;
        if (overPlanCaptureLimit) return false;
        if (perRequest == true) return true;
        return projectCaptureEnabled && captureMessages;
    }

    public static string DisplayPlan(string status) => status switch
    {
        "team" => "team",
        "paid" or "pro" or "beta_premium" => "pro",
        "past_due" => "past_due",
        _ => "free"
    };

    public static int MonthlyCaptureLimit(string status) => DisplayPlan(status) switch
    {
        "team" => TeamMonthlyLimit,
        "pro" => ProMonthlyLimit,
        _ => FreeMonthlyLimit
    };

    public static int MaxRetentionDays(string status) => DisplayPlan(status) switch
    {
        "team" => 90,
        "pro" => 30,
        _ => 3
    };

    public static IReadOnlyList<int> AllowedRetentionDays(string status)
    {
        var max = MaxRetentionDays(status);
        return new[] { 3, 7, 30, 90 }.Where(d => d <= max).ToArray();
    }
}
