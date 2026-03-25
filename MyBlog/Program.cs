using Google.Cloud.Firestore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.FileProviders;
using MyBlog.Services;

var builder = WebApplication.CreateBuilder(args);
var secureCookiePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
var appVersion =
    Environment.GetEnvironmentVariable("APP_VERSION")
    ?? builder.Configuration["App:Version"]
    ?? "unknown";
if (!TryParseAppMode(
        Environment.GetEnvironmentVariable("APP_MODE") ?? builder.Configuration["App:Mode"],
        out var appMode))
{
    throw new InvalidOperationException("Invalid App:Mode value. Supported values are Combined, BlogOnly, AislePilotOnly.");
}

// ----------------------------
// Cloud Run Port Binding
// ----------------------------
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ----------------------------
// Services
// ----------------------------
builder.Services.AddControllersWithViews();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "myblog.antiforgery.v2";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookiePolicy;
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "CookieAuth";
        options.DefaultChallengeScheme = "CookieAuth";
        options.DefaultSignInScheme = "CookieAuth";
        options.DefaultSignOutScheme = "CookieAuth";
    })
    .AddCookie("CookieAuth", options =>
    {
        options.LoginPath = "/Admin/Login";
        options.AccessDeniedPath = "/Admin/AccessDenied";
        options.Cookie.Name = "__session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = secureCookiePolicy;
        options.SlidingExpiration = true;
    });

var firestoreProjectId =
    Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ??
    builder.Configuration["Firestore:ProjectId"];
var firestoreDatabaseId =
    Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID") ??
    builder.Configuration["Firestore:DatabaseId"] ??
    "(default)";

var allowInMemoryFallback = builder.Environment.IsDevelopment();
var firestoreEnabled = false;

if (string.IsNullOrWhiteSpace(firestoreProjectId))
{
    if (!allowInMemoryFallback)
    {
        throw new InvalidOperationException(
            "Firestore project id is missing in non-development environment. Set GOOGLE_CLOUD_PROJECT or Firestore:ProjectId.");
    }

    Console.WriteLine("Firestore project id not configured. Using in-memory comments/likes (development only).");
}
else
{
    try
    {
        var firestoreDb = new FirestoreDbBuilder
        {
            ProjectId = firestoreProjectId,
            DatabaseId = firestoreDatabaseId
        }.Build();

        builder.Services.AddSingleton(firestoreDb);
        firestoreEnabled = true;
        Console.WriteLine($"Firestore enabled for project '{firestoreProjectId}' and database '{firestoreDatabaseId}'.");
    }
    catch (Exception ex)
    {
        if (!allowInMemoryFallback)
        {
            throw new InvalidOperationException(
                "Firestore initialization failed in non-development environment. Check service credentials and Firestore config.",
                ex);
        }

        Console.WriteLine($"Firestore unavailable. Falling back to in-memory comments/likes (development only). {ex.Message}");
    }
}
var dataProtectionApplicationName =
    Environment.GetEnvironmentVariable("DATA_PROTECTION_APP_NAME") ??
    builder.Configuration["DataProtection:ApplicationName"] ??
    "myblog";

builder.Services.AddDataProtection().SetApplicationName(dataProtectionApplicationName);
if (firestoreEnabled)
{
    builder.Services.AddSingleton<IXmlRepository, FirestoreDataProtectionKeyRepository>();
    builder.Services.AddOptions<KeyManagementOptions>()
        .Configure<IXmlRepository>((options, repository) => { options.XmlRepository = repository; });
}

builder.Services.AddSingleton<BlogService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<LikeService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<SubscriptionEmailService>();
builder.Services.AddHttpClient<AislePilotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(75);
});
builder.Services.AddScoped<IAislePilotService>(sp => sp.GetRequiredService<AislePilotService>());
builder.Services.AddScoped<IAislePilotExportService, AislePilotExportService>();
builder.Services.AddHttpClient<AIModerationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddHttpClient<DailyCodingCapsuleService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddTransient<IDailyCodingCapsuleProvider>(sp => sp.GetRequiredService<DailyCodingCapsuleService>());

var openAiApiKey =
    builder.Configuration["OPENAI_API_KEY"] ??
    Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    Console.WriteLine("WARNING: OPENAI_API_KEY is not configured. All new comments will require manual review.");
}

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var request = context.HttpContext.Request;
        var isAjaxRequest = string.Equals(
            request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

        if (isAjaxRequest)
        {
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsync(
                "{\"success\":false,\"error\":\"Too many requests. Please try again shortly.\"}",
                cancellationToken);
            return;
        }

        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again shortly.",
            cancellationToken);
    };

    options.AddPolicy("commentWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("likeWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 50,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("contactWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("subscriptionWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("aislePilotWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 45,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("aislePilotAdminWarmupWrites", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

// ----------------------------
// Middleware
// ----------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");
        headers["X-App-Version"] = appVersion;

        var cachePolicy = ResolveCachePolicy(context);
        if (cachePolicy == CachePolicy.NoStore)
        {
            headers["Cache-Control"] = "no-store, no-cache, max-age=0, must-revalidate, private";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }
        else if (cachePolicy == CachePolicy.PrivateRevalidate)
        {
            headers["Cache-Control"] = "private, no-cache, max-age=0, must-revalidate";
            headers["Pragma"] = "no-cache";

            var varyValues = headers.Vary.ToString();
            if (string.IsNullOrWhiteSpace(varyValues))
            {
                headers["Vary"] = "Cookie";
            }
            else if (!varyValues.Contains("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                headers["Vary"] = $"{varyValues}, Cookie";
            }
        }

        return Task.CompletedTask;
    });

    await next();
});
app.Use(async (context, next) =>
{
    if (appMode == AppMode.Combined || IsRequestAllowedInMode(context.Request.Path, appMode))
    {
        await next();
        return;
    }

    if (appMode == AppMode.AislePilotOnly && IsRootPath(context.Request.Path))
    {
        context.Response.Redirect("/projects/aisle-pilot");
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
});

app.UseStaticFiles();
if (!string.IsNullOrWhiteSpace(app.Environment.WebRootPath))
{
    var aislePilotImageRoot = Path.Combine(app.Environment.WebRootPath, "images");
    if (Directory.Exists(aislePilotImageRoot))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(aislePilotImageRoot),
            RequestPath = "/projects/aisle-pilot/images"
        });
    }
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.StatusCode != StatusCodes.Status400BadRequest)
    {
        return;
    }

    if (!HttpMethods.IsPost(httpContext.Request.Method))
    {
        return;
    }

    if (string.Equals(
            httpContext.Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var requestPath = httpContext.Request.Path.Value ?? string.Empty;
    if (!ShouldRecoverFromBadRequest(requestPath))
    {
        return;
    }

    var returnPath = ResolveBadRequestReturnPath(httpContext.Request, requestPath);
    if (string.IsNullOrWhiteSpace(returnPath))
    {
        return;
    }

    var returnPathWithFlag = AddOrReplaceQueryParameter(returnPath, "form", "expired");
    httpContext.Response.Clear();
    httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
    httpContext.Response.Headers.Location = returnPathWithFlag;
});

// ----------------------------
// Routes
// ----------------------------
app.MapControllerRoute(
    name: "blogPost",
    pattern: "blog/{slug?}",
    defaults: new { controller = "Blog", action = "Post" });

app.MapControllerRoute(
    name: "blogIndex",
    pattern: "blog",
    defaults: new { controller = "Blog", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static string GetRateLimitPartitionKey(HttpContext context)
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers.UserAgent.ToString();

    if (string.IsNullOrWhiteSpace(ip))
    {
        ip = "unknown-ip";
    }

    var userAgentBucket = GetUserAgentBucket(userAgent);

    return $"{ip}|{userAgentBucket}";
}

static string GetUserAgentBucket(string? userAgent)
{
    if (string.IsNullOrWhiteSpace(userAgent))
    {
        return "unknown-ua";
    }

    var ua = userAgent.ToLowerInvariant();
    if (ua.Contains("bot", StringComparison.Ordinal) || ua.Contains("crawler", StringComparison.Ordinal))
    {
        return "bot";
    }

    if (ua.Contains("postmanruntime/", StringComparison.Ordinal))
    {
        return "postman";
    }

    if (ua.Contains("curl/", StringComparison.Ordinal))
    {
        return "curl";
    }

    if (ua.Contains("python-requests", StringComparison.Ordinal))
    {
        return "python-requests";
    }

    if (ua.Contains("edg/", StringComparison.Ordinal))
    {
        return "edge";
    }

    if (ua.Contains("opr/", StringComparison.Ordinal) || ua.Contains("opera/", StringComparison.Ordinal))
    {
        return "opera";
    }

    if (ua.Contains("firefox/", StringComparison.Ordinal))
    {
        return "firefox";
    }

    if (ua.Contains("chrome/", StringComparison.Ordinal))
    {
        return "chrome";
    }

    if (ua.Contains("safari/", StringComparison.Ordinal) && ua.Contains("version/", StringComparison.Ordinal))
    {
        return "safari";
    }

    return "other";
}

static CachePolicy ResolveCachePolicy(HttpContext context)
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

static bool ShouldRecoverFromBadRequest(string requestPath)
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

static string? ResolveBadRequestReturnPath(HttpRequest request, string requestPath)
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

static bool TryGetSafeLocalPath(string? referer, HostString requestHost, out string? localPath)
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

static string AddOrReplaceQueryParameter(string path, string key, string value)
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

static bool IsRequestAllowedInMode(PathString path, AppMode appMode)
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

static bool IsRootPath(PathString path)
{
    var value = path.Value ?? string.Empty;
    return string.IsNullOrWhiteSpace(value) || value.Equals("/", StringComparison.OrdinalIgnoreCase);
}

static bool IsStaticAssetPath(PathString path)
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

static bool TryParseAppMode(string? rawValue, out AppMode appMode)
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

enum CachePolicy
{
    None,
    PrivateRevalidate,
    NoStore
}

enum AppMode
{
    Combined,
    BlogOnly,
    AislePilotOnly
}

public partial class Program;
