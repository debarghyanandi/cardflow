using System.Security.Cryptography;
using System.Text;

namespace Cardflow.Api.Helpers;

/// <summary>
/// Reads and writes the anonymous session cookie that stands in for a login
/// (architecture.md §4 — join by link, no signup). Only the SHA-256 hash of
/// the cookie's secret is ever stored, in <c>BoardMember.SessionHash</c>; the
/// raw secret never leaves the browser.
/// </summary>
public static class SessionCookieHelper
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
