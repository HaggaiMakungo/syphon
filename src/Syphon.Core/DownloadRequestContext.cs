namespace Syphon.Core;

public sealed record DownloadCookie(string Name, string Value, string Domain, string Path = "/", bool Secure = false,
    bool HostOnly = true, double? Expires = null);

/// <summary>Ephemeral browser context. Never include it in a durable task or diagnostic.</summary>
public sealed record DownloadRequestContext(string Url, string? Referrer = null, string? UserAgent = null, DownloadCookie[]? Cookies = null)
{
    public void Validate(string downloadUrl)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var scope) || scope.Scheme is not ("http" or "https") ||
            scope.UserInfo.Length > 0 || Url != downloadUrl || Url.Length > 8192 || Cookies?.Length > 64)
            throw new ArgumentException("Invalid browser download context.");
        static bool Text(string? value, int max) => value is null || value.Length <= max && value.All(c => c >= 32 && c != 127);
        if (!Text(UserAgent, 512) || !Text(Referrer, 2048) || Referrer is not null &&
            (!Uri.TryCreate(Referrer, UriKind.Absolute, out var referer) || referer.Scheme is not ("http" or "https") || referer.UserInfo.Length > 0))
            throw new ArgumentException("Invalid browser request headers.");
        var size = 0;
        foreach (var cookie in Cookies ?? [])
        {
            if (cookie is null || string.IsNullOrEmpty(cookie.Name) || string.IsNullOrEmpty(cookie.Domain) ||
                cookie.Name.Length > 256 || cookie.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c)) ||
                cookie.Value is null || cookie.Value.Any(c => c < 33 || c > 126 || c is ';' or ',' or '"' or '\\') ||
                string.IsNullOrEmpty(cookie.Path) || !cookie.Path.StartsWith('/') || !Text(cookie.Path, 2048) ||
                cookie.Domain.Length > 253 || cookie.Domain.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or ':')) ||
                cookie.Expires is double expiry && !double.IsFinite(expiry))
                throw new ArgumentException("Invalid browser cookie scope.");
            var domain = cookie.Domain.TrimStart('.');
            if (cookie.HostOnly ? !scope.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) :
                !scope.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) && !scope.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Browser cookies do not match this download site.");
            size += cookie.Name.Length + cookie.Value.Length + cookie.Domain.Length + cookie.Path.Length;
        }
        if (size > 6000) throw new ArgumentException("The browser sign-in data is too large for this handoff.");
    }

    public string CookieHeader(Uri target)
    {
        var scope = new Uri(Url);
        if (!SameOrigin(scope, target)) return "";
        return string.Join("; ", (Cookies ?? []).Where(c => (!c.Secure || target.Scheme == "https") &&
            (c.Expires is null || c.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) &&
            (target.AbsolutePath == c.Path || target.AbsolutePath.StartsWith(c.Path, StringComparison.Ordinal) &&
                (c.Path.EndsWith('/') || target.AbsolutePath.Length > c.Path.Length && target.AbsolutePath[c.Path.Length] == '/')))
            .OrderByDescending(c => c.Path.Length).Select(c => c.Name + "=" + c.Value));
    }
    public static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.IdnHost.Equals(b.IdnHost, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}
