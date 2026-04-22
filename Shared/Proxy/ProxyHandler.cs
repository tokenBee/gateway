using System.Diagnostics;
using System.Text.Json;
using TokenBee.Features.Observability;
using TokenBee.Features.Compression;
using TokenBee.Features.Replay;

namespace TokenBee.Shared.Proxy;

public static class ProxyHandler
{
    public static async Task Handle(HttpContext ctx,IHttpClientFactory factory,ITraceLogger traceLogger,ICompressionClient compressionClient,ISpanRecorder spanRecorder)
    {
        try 
        {
            // ─── TokenBee Compression Headers ──────────────────────────────────────────
            // X-TokenBee-Compression: auto
            // X-TokenBee-Rate: 0.5
            // X-TokenBee-Model: gpt-4o (explicit override)
            // X-TokenBee-Privacy: true (disables all logging)
            // ────────────────────────────────────────────────────────────────────────
            var stopwatch = Stopwatch.StartNew();

            // 1. Require X-LLM-Key header
            var llmKey = ctx.Request.Headers["X-LLM-Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(llmKey))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-LLM-Key header" });
                return;
            }

            // 2. Extract metadata from headers
            var metadata = MetadataExtractor.Extract(ctx.Request.Headers);

            // Override UserId from middleware context (API key auth) with fallback to header
            var userId = ctx.Items["UserId"]?.ToString()
                ?? ctx.Request.Headers["X-TB-User-Id"].FirstOrDefault();
            metadata = metadata with { UserId = userId };

            // 3. Read request body
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();

            var compressionStr = ctx.Request.Headers["X-TokenBee-Compression"].FirstOrDefault()?.ToLowerInvariant();
            var rateStr = ctx.Request.Headers["X-TokenBee-Rate"].FirstOrDefault();
            var modelHeader = ctx.Request.Headers["X-TokenBee-Model"].FirstOrDefault();
            var providerHeader = ctx.Request.Headers["X-TokenBee-Provider"].FirstOrDefault();
            var privacyStr = ctx.Request.Headers["X-TokenBee-Privacy"].FirstOrDefault()?.ToLowerInvariant();
            
            bool isPrivate = privacyStr is "true" or "1";
            
            float rate = 0.5f;
            bool skipCompression = false;

            // 1. Check if explicitly disabled
            if (compressionStr is "off" or "none" or "false")
            {
                skipCompression = true;
            }
            // 2. Parse rate header
            else if (float.TryParse(rateStr, out float parsed)) 
            {
                rate = parsed;
            }

            // Auto skip if rate is functionally 1.0 (100% retaining)
            if (rate >= 1.0f) skipCompression = true;

            CompressionResult compression;
            if (skipCompression)
            {
                int estimatedTokens = body.Length / 4;
                compression = new CompressionResult(body, estimatedTokens, estimatedTokens, 0, 1.0, false);
            }
            else
            {
                compression = await compressionClient.CompressAsync(body, rate, ctx.RequestAborted);
            }
            
            body = compression.CompressedBody;

            // 4. Route to correct provider
            var model = !string.IsNullOrEmpty(modelHeader) 
                ? modelHeader 
                : ProviderRouter.ExtractModel(body);

            var provider = ProviderRouter.Route(model, llmKey, providerHeader);

            // 5. Detect streaming
            var isStreaming = DetectStreaming(body);

            // 6. Build destination URL
            var path = ctx.Request.RouteValues["path"]?.ToString();
            var destination = $"{provider.BaseUrl}/v1/{path}";

            // 7. Build outgoing request
            var client = factory.CreateClient("llm");
            var outgoing = new HttpRequestMessage(HttpMethod.Post, destination)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };

            outgoing.Headers.Add(provider.AuthHeader, provider.AuthValue);

            if (provider.ExtraHeaders is not null)
                foreach (var (key, value) in provider.ExtraHeaders)
                    outgoing.Headers.Add(key, value);

            // 8. Forward to provider
            HttpResponseMessage llmResponse;
            try
            {
                llmResponse = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 502;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    error = "LLM unreachable",
                    detail = ex.Message
                });
                return;
            }

            // 9. Return response and log trace
            ctx.Response.StatusCode = (int)llmResponse.StatusCode;
            ctx.Response.ContentType =
                llmResponse.Content.Headers.ContentType?.ToString()
                ?? "application/json";

            if (isStreaming)
            {
                // Streaming: pipe chunks directly, estimate tokens
                await llmResponse.Content.CopyToAsync(ctx.Response.Body);
                await ctx.Response.Body.FlushAsync();
                stopwatch.Stop();

                var outputTokens = 0;

                _ = LogTrace(traceLogger, path, model, metadata, outputTokens,
                    (int)llmResponse.StatusCode, stopwatch.ElapsedMilliseconds, isStreaming,
                    body, null, compression);

                // Record span for replay (fire-and-forget)
                if (!string.IsNullOrEmpty(metadata.SessionId) && !isPrivate)
                {
                    _ = spanRecorder.RecordLlmCallAsync(
                        sessionId:     metadata.SessionId,
                        inputPayload:  body,
                        outputPayload: string.Empty,
                        durationMs:    (int)stopwatch.ElapsedMilliseconds,
                        tokens:        compression.CompressedTokens + outputTokens,
                        model:         model,
                        wasCompressed: compression.WasCompressed,
                        savedTokens:   compression.SavedTokens);
                }
            }
            else
            {
                // Non-streaming: read full response, extract exact tokens
                var responseBody = await llmResponse.Content.ReadAsStringAsync();
                await ctx.Response.WriteAsync(responseBody);
                await ctx.Response.Body.FlushAsync();
                stopwatch.Stop();

                var (_, outputTokens) = ExtractTokens(responseBody);

                _ = LogTrace(traceLogger, path, model, metadata, outputTokens,
                    (int)llmResponse.StatusCode, stopwatch.ElapsedMilliseconds, isStreaming,
                    body, responseBody, compression);

                // Record span for replay (fire-and-forget)
                if (!string.IsNullOrEmpty(metadata.SessionId) && !isPrivate)
                {
                    _ = spanRecorder.RecordLlmCallAsync(
                        sessionId:     metadata.SessionId,
                        inputPayload:  body,
                        outputPayload: responseBody,
                        durationMs:    (int)stopwatch.ElapsedMilliseconds,
                        tokens:        compression.CompressedTokens + outputTokens,
                        model:         model,
                        wasCompressed: compression.WasCompressed,
                        savedTokens:   compression.SavedTokens);
                }
            }
        }
        catch (Exception ex) 
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { error = "Internal Proxy Error", detail = ex.Message, stack = ex.StackTrace });
        }
    }

    private const int MaxBodyLength = 10_000;

    private static string? Truncate(string? value) =>
        value is not null && value.Length > MaxBodyLength
            ? value[..MaxBodyLength]
            : value;

    private static Task LogTrace(ITraceLogger traceLogger,string? path,string model,RequestMetadata metadata,int outputTokens,int statusCode,long latencyMs,bool isStreaming,string? requestBody,string? responseBody, CompressionResult compression)
    {
        var (inputCost, outputCost, totalCost) =
            CostCalculator.Calculate(model, compression.CompressedTokens, outputTokens);
            
        var (origInputCost, _, origTotalCost) =
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
            SessionId = metadata.SessionId,
            RequestBody = Truncate(requestBody),
            ResponseBody = Truncate(responseBody)
        };

        if (compression.WasCompressed)
        {
            var compMeta = new
            {
                compressionMode = compression.ModeUsed,
                compressionQuery = compression.QueryUsed,
                autoQuery = compression.AutoQuery
            };
            trace.CompressionMetadataJson = JsonSerializer.Serialize(compMeta);
        }

        trace.SetProperties(metadata.Properties);

        return traceLogger.LogAsync(trace);
    }

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
        if (m.StartsWith("llama-") || m.StartsWith("deepseek-r1-distill")) return "groq";
        if (m.StartsWith("grok-")) return "xai";

        return "openai";
    }
}
