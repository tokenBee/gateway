namespace TokenBee.Shared.Proxy
{

    public record ProviderConfig(string BaseUrl,string AuthHeader, string AuthValue, Dictionary<string, string>? ExtraHeaders = null);

    public static class ProviderRouter
    {
        public static string ExtractModel(string requestBody)
        {
            // 1. Try standard JSON parsing
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(requestBody);
                if (doc.RootElement.TryGetProperty("model", out var m))
                {
                    return m.GetString() ?? "unknown";
                }
            }
            catch
            {
                // Fall through to regex if JSON is malformed (e.g. unescaped newlines)
            }

            // 2. Regex fallback for robust routing
            var match = System.Text.RegularExpressions.Regex.Match(requestBody, "\"model\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "unknown";
        }

        public static ProviderConfig Route(string model, string llmKey)
        {
            var m = model.ToLowerInvariant();

            // Anthropic
            if (m.StartsWith("claude-"))
                return new ProviderConfig(
                    BaseUrl: "https://api.anthropic.com",
                    AuthHeader: "x-api-key",
                    AuthValue: llmKey,
                    ExtraHeaders: new Dictionary<string, string>
                    {
                        ["anthropic-version"] = "2023-06-01"
                    }
                );

            // Perplexity
            if (m.Contains("sonar") || m.Contains("llama-3-sonar"))
                return new ProviderConfig(
                    BaseUrl: "https://api.perplexity.ai",
                    AuthHeader: "Authorization",
                    AuthValue: $"Bearer {llmKey}"
                );

            // Mistral
            if (m.StartsWith("mistral-") || m.StartsWith("open-mixtral") || m.StartsWith("pixtral-"))
                return new ProviderConfig(
                    BaseUrl: "https://api.mistral.ai",
                    AuthHeader: "Authorization",
                    AuthValue: $"Bearer {llmKey}"
                );

            // Gemini (OpenAI Compatible)
            if (m.Contains("gemini-"))
                return new ProviderConfig(
                    BaseUrl: "https://generativelanguage.googleapis.com/v1beta/openai",
                    AuthHeader: "Authorization",
                    AuthValue: $"Bearer {llmKey}"
                );

            // Groq
            if (m.StartsWith("llama-") ||
                m.StartsWith("mixtral-") ||
                m.StartsWith("gemma-"))
                return new ProviderConfig(
                    BaseUrl: "https://api.groq.com/openai",
                    AuthHeader: "Authorization",
                    AuthValue: $"Bearer {llmKey}"
                );

            // OpenAI default
            return new ProviderConfig(
                BaseUrl: "https://api.openai.com",
                AuthHeader: "Authorization",
                AuthValue: $"Bearer {llmKey}"
            );
        }
}
}
