using System.Diagnostics;
using System.Text.Json;
using TokenScope.Features.Observability;

namespace TokenScope.Shared.Proxy;

public static class ProxyHandler
{
    public static async Task Handle(HttpContext ctx,IHttpClientFactory factory,ITraceLogger traceLogger)
    {
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

        // 3. Read request body
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();

        // 4. Route to correct provider
        var model = ProviderRouter.ExtractModel(body);
        var provider = ProviderRouter.Route(model, llmKey);

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
            stopwatch.Stop();

            var inputTokens = body.Length / 4;
            var outputTokens = 0;

            _ = LogTrace(traceLogger, path, model, metadata, inputTokens, outputTokens,
                (int)llmResponse.StatusCode, stopwatch.ElapsedMilliseconds, isStreaming,
                body, null);
        }
        else
        {
            // Non-streaming: read full response, extract exact tokens
            var responseBody = await llmResponse.Content.ReadAsStringAsync();
            await ctx.Response.WriteAsync(responseBody);
            stopwatch.Stop();

            var (inputTokens, outputTokens) = ExtractTokens(responseBody);

            _ = LogTrace(traceLogger, path, model, metadata, inputTokens, outputTokens,
                (int)llmResponse.StatusCode, stopwatch.ElapsedMilliseconds, isStreaming,
                body, responseBody);
        }
    }

    private const int MaxBodyLength = 10_000;

    private static string? Truncate(string? value) =>
        value is not null && value.Length > MaxBodyLength
            ? value[..MaxBodyLength]
            : value;

    private static Task LogTrace(ITraceLogger traceLogger,string? path,string model,RequestMetadata metadata,int inputTokens,int outputTokens,int statusCode,long latencyMs,bool isStreaming,string? requestBody,string? responseBody)
    {
        var (inputCost, outputCost, totalCost) =
            CostCalculator.Calculate(model, inputTokens, outputTokens);

        var trace = new TraceRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Path = path ?? string.Empty,
            Model = model,
            Provider = DetectProviderName(model),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            OriginalTokens = inputTokens,
            CompressedTokens = inputTokens,
            InputCostUsd = inputCost,
            OutputCostUsd = outputCost,
            TotalCostUsd = totalCost,
            SavedCostUsd = 0m,
            LatencyMs = (int)latencyMs,
            StatusCode = statusCode,
            WasCompressed = false,
            IsStreaming = isStreaming,
            UserId = metadata.UserId,
            SessionId = metadata.SessionId,
            RequestBody = Truncate(requestBody),
            ResponseBody = Truncate(responseBody)
        };

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
        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return "anthropic";

        if (model.StartsWith("llama-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("mixtral-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gemma-", StringComparison.OrdinalIgnoreCase))
            return "groq";

        return "openai";
    }
}