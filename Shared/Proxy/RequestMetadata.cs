namespace TokenBee.Shared.Proxy;

public record RequestMetadata
(
    string? UserId,
    string? AccountId,
    string? SessionId,
    Dictionary<string, string> Properties
);

public static class MetadataExtractor
{
    private const string UserIdHeader = "X-TB-User-Id";
    private const string SessionIdHeader = "X-TB-Session-Id";
    private const string PropertyPrefix = "X-TB-Property-";

    public static RequestMetadata Extract(IHeaderDictionary headers)
    {
        var userId = headers[UserIdHeader].FirstOrDefault();
        var sessionId = headers[SessionIdHeader].FirstOrDefault();

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Collect all X-TB-Property-* headers
        foreach (var header in headers)
        {
            if (header.Key.StartsWith(PropertyPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                var propName = header.Key[PropertyPrefix.Length..];
                properties[propName] = header.Value.ToString();
            }
        }

        return new RequestMetadata(userId, null, sessionId, properties);
    }
}
