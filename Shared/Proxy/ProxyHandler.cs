using System.Diagnostics;
using System.Text.Json;
using TokenBee.Features.Auth;
using TokenBee.Features.Observability;
using TokenBee.Features.Compression;
using TokenBee.Features.Replay;

namespace TokenBee.Shared.Proxy;

public static class ProxyHandler
{
    public static async Task Handle(
        HttpContext ctx,
        IHttpClientFactory factory,
        ITraceLogger traceLogger,
        ICompressionClient compressionClient,
        ISpanRecorder spanRecorder,
        ISubscriptionService subscriptionService,
        ICaptureSettingsService captureSettings,
        MetricsQueries metricsQueries)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // 1. Validate LLM provider key
            var llmKey = ctx.Request.Headers["X-LLM-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(llmKey))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-LLM-Key header" });
                return;
            }

            // 2. Extract request metadata and user context
            var metadata = MetadataExtractor.Extract(ctx.Request.Headers);
            var accountId = ctx.Items["UserId"]?.ToString();
            metadata = metadata with { AccountId = accountId };

            var sub = ctx.Items["Subscription"] as SubscriptionStatus;

            // 3. Read request body and parse headers
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            var compressionStr = ctx.Request.Headers["X-TokenBee-Compression"].FirstOrDefault()?.ToLowerInvariant();
            var rateStr = ctx.Request.Headers["X-TokenBee-Rate"].FirstOrDefault();
            var modelHeader = ctx.Request.Headers["X-TokenBee-Model"].FirstOrDefault();
            var providerHeader = ctx.Request.Headers["X-TokenBee-Provider"].FirstOrDefault();
            var strategyStr = ctx.Request.Headers["X-TokenBee-Strategy"].FirstOrDefault()?.ToLowerInvariant();
            var contextStr = ctx.Request.Headers["X-TokenBee-Context"].FirstOrDefault()?.ToLowerInvariant() ?? "auto";
            var captureStr = ctx.Request.Headers["X-TokenBee-Capture"].FirstOrDefault();

            var planStatus = sub?.Status ?? "free";
            var settings = string.IsNullOrEmpty(accountId)
                ? new CaptureSettings("", true, CaptureDecision.MaxRetentionDays(planStatus), true)
                : await captureSettings.GetOrCreateAsync(accountId, planStatus);

            var capturedThisMonth = string.IsNullOrEmpty(accountId)
                ? 0
                : await metricsQueries.CountCapturedThisMonthAsync(accountId);
            var overCaptureLimit = capturedThisMonth >= CaptureDecision.MonthlyCaptureLimit(planStatus);

            var storeContent = CaptureDecision.ShouldStoreContent(
                captureStr, settings.CaptureEnabled, settings.CaptureMessages, overCaptureLimit);

            // 4. Determine compression settings
            float rate = 0.5f;
            bool skipCompression = false;

            if (compressionStr is "off" or "none" or "false")
                skipCompression = true;
            else if (float.TryParse(rateStr, out float parsed))
                rate = parsed;

            if (rate >= 1.0f) skipCompression = true;
            
            var strategy = CompressionStrategy.Smart; // Default to Smart (query-aware)
            if (strategyStr == "hive" || strategyStr == "hive_v1")
                strategy = CompressionStrategy.Hive;
            else if (strategyStr == "smart" || strategyStr == "smart_v1")
                strategy = CompressionStrategy.Smart;

            // Enforce tier limits:
            // 1. Free users are clamped to standard compression (0.50)
            // 2. Users OVER the free limit have compression DISABLED (Graceful degradation to observability-only)
            if (sub?.Status == "free")
            {
                if (sub.IsOverFreeLimit)
                    skipCompression = true;
                else if (rate < 0.5f)
                    rate = 0.5f;
            }

            // 5. Compress prompt (or skip if below threshold / disabled)
            CompressionResult compression;
            if (skipCompression)
            {
                int estimatedTokens = body.Length / 4;
                compression = new CompressionResult(body, estimatedTokens, estimatedTokens, 0, 1.0, false);
            }
            else
            {
                compression = await compressionClient.CompressAsync(body, rate, strategy, contextStr, ctx.RequestAborted);
            }

            var originalBody = body;
            body = compression.CompressedBody;

            // 6. Route to the correct LLM provider
            var model = !string.IsNullOrEmpty(modelHeader)
                ? modelHeader
                : ProviderRouter.ExtractModel(body);
            var provider = ProviderRouter.Route(model, llmKey, providerHeader);
            var isStreaming = DetectStreaming(body);

            // 7. Build and send the outgoing request
            var path = ctx.Request.RouteValues["path"]?.ToString();
            var destination = $"{provider.BaseUrl}/v1/{path}";

            var client = factory.CreateClient("llm");
            var outgoing = new HttpRequestMessage(HttpMethod.Post, destination)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };

            outgoing.Headers.Add(provider.AuthHeader, provider.AuthValue);
            if (provider.ExtraHeaders is not null)
                foreach (var (key, value) in provider.ExtraHeaders)
                    outgoing.Headers.Add(key, value);

            HttpResponseMessage llmResponse;
            try
            {
                llmResponse = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception)
            {
                ctx.Response.StatusCode = 502;
                await ctx.Response.WriteAsJsonAsync(new { error = "LLM provider unreachable" });
                return;
            }

            // 8. Stream or buffer the response back to the caller
            ctx.Response.StatusCode = (int)llmResponse.StatusCode;
            ctx.Response.ContentType =
                llmResponse.Content.Headers.ContentType?.ToString()
                ?? "application/json";

            string? responseBody;
            int outputTokens;

            if (isStreaming)
            {
                await llmResponse.Content.CopyToAsync(ctx.Response.Body);
                await ctx.Response.Body.FlushAsync();
                stopwatch.Stop();
                responseBody = null;
                outputTokens = 0;
            }
            else
            {
                responseBody = await llmResponse.Content.ReadAsStringAsync();
                await ctx.Response.WriteAsync(responseBody);
                await ctx.Response.Body.FlushAsync();
                stopwatch.Stop();
                (_, outputTokens) = ExtractTokens(responseBody);
            }

            // 9. Record trace, replay span, and token usage (fire-and-forget)
            RecordObservability(
                traceLogger, spanRecorder, subscriptionService, sub,
                path, model, metadata, accountId, storeContent,
                settings.RetentionDays,
                outputTokens, (int)llmResponse.StatusCode,
                stopwatch.ElapsedMilliseconds, isStreaming,
                body, responseBody, originalBody, compression);
        }
        catch (Exception)
        {
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsJsonAsync(new { error = "Internal server error" });
            }
        }
    }

    // ─── Post-Response Observability (Single Responsibility) ────────

    private static void RecordObservability(
        ITraceLogger traceLogger,
        ISpanRecorder spanRecorder,
        ISubscriptionService subscriptionService,
        SubscriptionStatus? sub,
        string? path,
        string model,
        RequestMetadata metadata,
        string? userId,
        bool storeContent,
        int retentionDays,
        int outputTokens,
        int statusCode,
        long latencyMs,
        bool isStreaming,
        string? requestBody,
        string? responseBody,
        string? originalRequestBody,
        CompressionResult compression)
    {
        // Trace logging
        _ = LogTrace(traceLogger, path, model, metadata, outputTokens,
            statusCode, latencyMs, isStreaming,
            storeContent ? requestBody : null,
            storeContent ? responseBody : null,
            storeContent ? originalRequestBody : null,
            compression, storeContent, retentionDays);

        // Session replay span (Premium feature: disabled if free limit exceeded or capture off)
        bool skipPremiumFeatures = sub?.Status == "free" && sub.IsOverFreeLimit;

        if (!string.IsNullOrEmpty(metadata.SessionId) && storeContent && !skipPremiumFeatures)
        {
            _ = spanRecorder.RecordLlmCallAsync(
                sessionId:     metadata.SessionId,
                inputPayload:  requestBody ?? string.Empty,
                outputPayload: responseBody ?? string.Empty,
                durationMs:    (int)latencyMs,
                tokens:        compression.CompressedTokens + outputTokens,
                model:         model,
                wasCompressed: compression.WasCompressed,
                savedTokens:   compression.SavedTokens);
        }

        // Token-based usage billing
        var totalTokens = compression.CompressedTokens + outputTokens;
        if (!string.IsNullOrEmpty(userId) && totalTokens > 0)
        {
            _ = Task.Run(async () =>
            {
                try { await subscriptionService.IncrementUsageAsync(userId, totalTokens); }
                catch { /* background task — swallow to prevent unobserved exceptions */ }
            });
        }
    }

    // ─── Trace Logging ─────────────────────────────────────────────

    private const int MaxBodyLength = 10_000;

    private static string? Truncate(string? value) =>
        value is not null && value.Length > MaxBodyLength
            ? value[..MaxBodyLength]
            : value;

    private static Task LogTrace(
        ITraceLogger traceLogger,
        string? path,
        string model,
        RequestMetadata metadata,
        int outputTokens,
        int statusCode,
        long latencyMs,
        bool isStreaming,
        string? requestBody,
        string? responseBody,
        string? originalRequestBody,
        CompressionResult compression,
        bool storeContent,
        int retentionDays)
    {
        var (inputCost, outputCost, totalCost) =
            CostCalculator.Calculate(model, compression.CompressedTokens, outputTokens);

        var (_, _, origTotalCost) =
            CostCalculator.Calculate(model, compression.OriginalTokens, outputTokens);

        var trace = new TraceRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Path = path ?? string.Empty,
            Model = model,
            Provider = DetectProviderName(model),
            InputTokens = compression.CompressedTokens,
            OutputTokens = outputTokens,
            OriginalTokens = compression.OriginalTokens,
            CompressedTokens = compression.CompressedTokens,
            InputCostUsd = inputCost,
            OutputCostUsd = outputCost,
            TotalCostUsd = totalCost,
            SavedCostUsd = origTotalCost - totalCost,
            LatencyMs = (int)latencyMs,
            StatusCode = statusCode,
            WasCompressed = compression.WasCompressed,
            IsStreaming = isStreaming,
            UserId = metadata.UserId,
            AccountId = metadata.AccountId,
            SessionId = metadata.SessionId,
            RequestBody = Truncate(requestBody),
            OriginalRequestBody = Truncate(originalRequestBody),
            ResponseBody = Truncate(responseBody),
            CaptureEnabled = storeContent,
            ExpiresAt = DateTime.UtcNow.AddDays(Math.Max(retentionDays, 1))
        };

        if (compression.WasCompressed)
        {
            var compMeta = new
            {
                compressionMode = compression.ModeUsed,
                compressionQuery = compression.QueryUsed,
                autoQuery = compression.AutoQuery,
                contextType = compression.ContextType
            };
            trace.CompressionMetadataJson = JsonSerializer.Serialize(compMeta);
        }

        trace.SetProperties(metadata.Properties);
        return traceLogger.LogAsync(trace);
    }

    // ─── Response Parsing Helpers ──────────────────────────────────

    private static (int InputTokens, int OutputTokens) ExtractTokens(string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                var input = usage.TryGetProperty("prompt_tokens", out var pt)
                    ? pt.GetInt32() : responseBody.Length / 4;
                var output = usage.TryGetProperty("completion_tokens", out var ct)
                    ? ct.GetInt32() : responseBody.Length / 4;
                return (input, output);
            }
        }
        catch
        {
            // Fall through to estimate
        }

        return (responseBody.Length / 4, responseBody.Length / 4);
    }

    private static bool DetectStreaming(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("stream", out var s)
                && s.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private static string DetectProviderName(string model)
    {
        var m = model.ToLowerInvariant();
        if (m.StartsWith("claude-")) return "anthropic";
        if (m.Contains("sonar")) return "perplexity";
        if (m.StartsWith("mistral-") || m.StartsWith("pixtral-")) return "mistral";
        if (m.Contains("gemini-")) return "google";
        if (m.StartsWith("grok-")) return "xai";
        // Groq-hosted IDs (incl. openai/gpt-oss-*, qwen/*, compound)
        if (m.StartsWith("llama-") ||
            m.StartsWith("meta-llama/") ||
            m.StartsWith("deepseek-") ||
            m.StartsWith("qwen/") ||
            m.StartsWith("qwen") ||
            m.Contains("gpt-oss") ||
            m.Contains("compound") ||
            m.StartsWith("moonshotai/") ||
            m.StartsWith("gemma-") ||
            m.StartsWith("mixtral-"))
            return "groq";

        return "openai";
    }
}
