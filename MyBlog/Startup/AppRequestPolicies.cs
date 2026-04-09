using Microsoft.AspNetCore.WebUtilities;

namespace MyBlog.Startup;

internal static class AppRequestPolicies
{
    public static CachePolicy ResolveCachePolicy(HttpContext context)
    {
        if (context.Response.Headers.ContainsKey("Set-Cookie"))
        {
            return CachePolicy.NoStore;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return CachePolicy.None;
        }

        if (path.Equals("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/subscribe", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/subscribe/", StringComparison.OrdinalIgnoreCase))
        {
            return CachePolicy.NoStore;
        }

        if (path.Equals("/blog", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase))
        {
            return CachePolicy.PrivateRevalidate;
        }

        return CachePolicy.None;
    }

    public static bool ShouldRecoverFromBadRequest(string requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return false;
        }

        return requestPath.Equals("/Admin/Logout", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Admin/Approve", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Admin/Delete", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Blog/AddComment", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Blog/TogglePostLike", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Blog/ToggleCommentLike", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot/rebalance-budget", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot/suggest-from-pantry", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot/export/plan-pack", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/projects/aisle-pilot/export/checklist", StringComparison.OrdinalIgnoreCase)
               || requestPath.Equals("/Subscribe", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveBadRequestReturnPath(HttpRequest request, string requestPath)
    {
        var referer = request.Headers.Referer.FirstOrDefault();
        if (TryGetSafeLocalPath(referer, request.Host, out var localPath))
        {
            return localPath;
        }

        if (requestPath.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            return requestPath.Equals("/Admin/Login", StringComparison.OrdinalIgnoreCase)
                ? "/Admin/Login"
                : "/Admin";
        }

        if (requestPath.StartsWith("/Blog", StringComparison.OrdinalIgnoreCase))
        {
            return "/blog";
        }

        if (requestPath.StartsWith("/ai-experiments", StringComparison.OrdinalIgnoreCase))
        {
            return "/ai-experiments";
        }

        if (requestPath.StartsWith("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase))
        {
            return "/projects/aisle-pilot";
        }

        if (requestPath.Equals("/Subscribe", StringComparison.OrdinalIgnoreCase))
        {
            return "/blog";
        }

        return null;
    }

    public static bool TryGetSafeLocalPath(string? referer, HostString requestHost, out string? localPath)
    {
        localPath = null;
        if (string.IsNullOrWhiteSpace(referer))
        {
            return false;
        }

        if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            return false;
        }

        if (!string.Equals(refererUri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        localPath = $"{refererUri.AbsolutePath}{refererUri.Query}";
        return !string.IsNullOrWhiteSpace(localPath);
    }

    public static string AddOrReplaceQueryParameter(string path, string key, string value)
    {
        var anchorIndex = path.IndexOf('#');
        var anchor = anchorIndex >= 0 ? path[anchorIndex..] : string.Empty;
        var pathWithoutAnchor = anchorIndex >= 0 ? path[..anchorIndex] : path;
        var questionMarkIndex = pathWithoutAnchor.IndexOf('?');
        var basePath = questionMarkIndex >= 0 ? pathWithoutAnchor[..questionMarkIndex] : pathWithoutAnchor;
        var rawQuery = questionMarkIndex >= 0 ? pathWithoutAnchor[(questionMarkIndex + 1)..] : string.Empty;

        var queryValues = QueryHelpers.ParseQuery(rawQuery);
        var flattenedValues = new List<KeyValuePair<string, string?>>();
        foreach (var pair in queryValues)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in pair.Value)
            {
                if (item is null)
                {
                    continue;
                }

                flattenedValues.Add(new KeyValuePair<string, string?>(pair.Key, item));
            }
        }

        flattenedValues.Add(new KeyValuePair<string, string?>(key, value));
        var queryString = QueryString.Create(flattenedValues).ToUriComponent();
        return $"{basePath}{queryString}{anchor}";
    }

    public static bool IsRequestAllowedInMode(PathString path, AppMode appMode)
    {
        if (appMode == AppMode.Combined)
        {
            return true;
        }

        if (IsStaticAssetPath(path))
        {
            return true;
        }

        var value = path.Value ?? string.Empty;
        if (value.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var isAislePilotPath =
            value.Equals("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/projects/aisle-pilot/", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/admin/aisle-pilot/warmup", StringComparison.OrdinalIgnoreCase);

        return appMode switch
        {
            AppMode.BlogOnly => !isAislePilotPath,
            AppMode.AislePilotOnly => isAislePilotPath,
            _ => true
        };
    }

    public static bool IsRootPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) || value.Equals("/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStaticAssetPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
               || value.Equals("/myblog.styles.css", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseAppMode(string? rawValue, out AppMode appMode)
    {
        var normalized = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            appMode = AppMode.Combined;
            return true;
        }

        if (Enum.TryParse(normalized, ignoreCase: true, out appMode))
        {
            return true;
        }

        appMode = AppMode.Combined;
        return false;
    }
}

internal enum CachePolicy
{
    None,
    PrivateRevalidate,
    NoStore
}

internal enum AppMode
{
    Combined,
    BlogOnly,
    AislePilotOnly
}
