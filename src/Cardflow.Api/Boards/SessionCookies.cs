using System.Security.Cryptography;
using System.Text;

namespace Cardflow.Api.Boards;

public static class SessionCookies
{
    public const string Name = "cardflow_session";

    public static string Existing(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Name, out var value) ? value : "";

    public static string ExistingOrNew(HttpContext context) =>
        context.Request.Cookies.TryGetValue(Name, out var value) && value.Length == 64
            ? value
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static void Set(HttpContext context, string secret)
    {
        context.Response.Cookies.Append(Name, secret, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    }
}
