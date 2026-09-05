using TokenBee.Features.Observability;
using Xunit;

namespace TokenBee.Tests;

public class CaptureDecisionTests
{
    [Fact]
    public void PerRequestFalse_NeverStoresContent()
    {
        Assert.False(CaptureDecision.ShouldStoreContent("false", true, true, false));
        Assert.False(CaptureDecision.ShouldStoreContent("off", true, true, false));
    }

    [Fact]
    public void PerRequestTrue_StoresUnlessOverLimit()
    {
        Assert.True(CaptureDecision.ShouldStoreContent("true", false, false, false));
        Assert.False(CaptureDecision.ShouldStoreContent("true", true, true, true));
    }

    [Fact]
    public void ProjectCaptureOff_DoesNotStore()
    {
        Assert.False(CaptureDecision.ShouldStoreContent(null, false, true, false));
        Assert.False(CaptureDecision.ShouldStoreContent(null, true, false, false));
    }

    [Fact]
    public void Default_StoresContent()
    {
        Assert.True(CaptureDecision.ShouldStoreContent(null, true, true, false));
    }

    [Fact]
    public void PlanLimits_AreInteractionBased()
    {
        Assert.Equal(1_000, CaptureDecision.MonthlyCaptureLimit("free"));
        Assert.Equal(25_000, CaptureDecision.MonthlyCaptureLimit("paid"));
        Assert.Equal(25_000, CaptureDecision.MonthlyCaptureLimit("pro"));
        Assert.Equal(100_000, CaptureDecision.MonthlyCaptureLimit("team"));
        Assert.Equal(3, CaptureDecision.MaxRetentionDays("free"));
        Assert.Equal(30, CaptureDecision.MaxRetentionDays("paid"));
        Assert.Equal(90, CaptureDecision.MaxRetentionDays("team"));
        Assert.Equal(new[] { 3 }, CaptureDecision.AllowedRetentionDays("free"));
        Assert.Equal(new[] { 3, 7, 30 }, CaptureDecision.AllowedRetentionDays("pro"));
        Assert.Equal(new[] { 3, 7, 30, 90 }, CaptureDecision.AllowedRetentionDays("team"));
    }
}
