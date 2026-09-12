namespace TokenBee.Shared.Auth;

/// <summary>
/// Requires a valid Supabase session for dashboard, replay, and account APIs.
/// Account identity always comes from the JWT — query/body user ids are ignored.
/// </summary>
public class DashboardAuthMiddleware(RequestDelegate next)
{
    private static readonly string[] ProtectedPrefixes =
    [
        "/api/dashboard",
        "/api/replay",
        "/api/auth"
    ];

    public async Task InvokeAsync(HttpContext ctx, SupabaseUserService supabaseUsers)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (HttpMethods.IsOptions(ctx.Request.Method) || !IsProtected(path))
        {
            await next(ctx);
            return;
        }

        var token = ReadBearer(ctx);
        if (string.IsNullOrEmpty(token))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Sign in required." });
            return;
        }

        if (!supabaseUsers.IsConfigured)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "Dashboard auth is not configured. Set Supabase__Url and Supabase__AnonKey."
            });
            return;
        }

        var userId = await supabaseUsers.GetUserIdAsync(token, ctx.RequestAborted);
        if (string.IsNullOrEmpty(userId))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or expired session." });
            return;
        }

        ctx.Items["AccountId"] = userId;
        await next(ctx);
    }

    private static bool IsProtected(string path)
    {
        foreach (var prefix in ProtectedPrefixes)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadBearer(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            return header["Bearer ".Length..].Trim();
        return null;
    }
}

public static class DashboardPrincipal
{
    public static string? AccountId(this HttpContext ctx) =>
        ctx.Items.TryGetValue("AccountId", out var value) ? value as string : null;

    public static string RequireAccountId(this HttpContext ctx) =>
        ctx.AccountId() ?? throw new InvalidOperationException("Authenticated account is missing.");
}
