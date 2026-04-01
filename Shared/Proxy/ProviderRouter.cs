namespace TokenScope.Shared.Proxy
{

    public record ProviderConfig(string BaseUrl,string AuthHeader, string AuthValue, Dictionary<string, string>? ExtraHeaders = null);

    public static class ProviderRouter
    {
        public static string ExtractModel(string requestBody)
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(requestBody);
                return doc.RootElement.TryGetProperty("model", out var m)? m.GetString() ?? "unknown": "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        public static ProviderConfig Route(string model, string llmKey)
        {
            // Anthropic
            if (model.StartsWith("claude-"))
                return new ProviderConfig(
                    BaseUrl: "https://api.anthropic.com",
                    AuthHeader: "x-api-key",
                    AuthValue: llmKey,
                    ExtraHeaders: new Dictionary<string, string>
                    {
                        ["anthropic-version"] = "2023-06-01"
                    }
                );

            // Groq
            if (model.StartsWith("llama-") ||
                model.StartsWith("mixtral-") ||
                model.StartsWith("gemma-"))
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
