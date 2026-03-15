using Google.Cloud.Firestore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using MyBlog.Services;

var builder = WebApplication.CreateBuilder(args);
var secureCookiePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

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
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.LoginPath = "/Admin/Login";
        options.AccessDeniedPath = "/Admin/AccessDenied";
        options.Cookie.Name = "myblog.auth.v1";
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
builder.Services.AddHttpClient<AIModerationService>();

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
        return Task.CompletedTask;
    });

    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        var firstForwarded = forwardedFor.Split(',')[0].Trim();
        if (!string.IsNullOrWhiteSpace(firstForwarded))
        {
            return firstForwarded;
        }
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

public partial class Program;
