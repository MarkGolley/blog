using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using MyBlog.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MyBlog.Startup;

internal static class ServiceCollectionStartupExtensions
{
    public static WebApplicationBuilder AddMyBlogApplicationServices(
        this WebApplicationBuilder builder,
        CookieSecurePolicy secureCookiePolicy)
    {
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
                options.Events = new CookieAuthenticationEvents
                {
                    OnSignedIn = async context =>
                    {
                        await RecordAuthEventAsync(
                            context.HttpContext,
                            eventName: "cookie_signed_in",
                            success: true);
                    },
                    OnSigningOut = async context =>
                    {
                        await RecordAuthEventAsync(
                            context.HttpContext,
                            eventName: "cookie_signing_out",
                            success: true);
                    },
                    OnRedirectToLogin = async context =>
                    {
                        await RecordAuthEventAsync(
                            context.HttpContext,
                            eventName: "cookie_redirect_to_login",
                            success: false,
                            reason: "authentication_required");
                        context.Response.Redirect(context.RedirectUri);
                    },
                    OnRedirectToAccessDenied = async context =>
                    {
                        await RecordAuthEventAsync(
                            context.HttpContext,
                            eventName: "cookie_redirect_to_access_denied",
                            success: false,
                            reason: "access_denied");
                        context.Response.Redirect(context.RedirectUri);
                    }
                };
            });

        ConfigureFirestore(builder);
        ConfigureApplicationServices(builder);

        return builder;
    }

    private static void ConfigureFirestore(WebApplicationBuilder builder)
    {
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
    }

    private static void ConfigureApplicationServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BlogService>();
        builder.Services.AddSingleton<IAislePilotPlanGenerationOrchestrator, AislePilotPlanGenerationOrchestrator>();
        builder.Services.AddSingleton<IAislePilotPlanComparisonService, AislePilotPlanComparisonService>();
        builder.Services.AddSingleton<IAislePilotBudgetRebalancePipeline, AislePilotBudgetRebalancePipeline>();
        builder.Services.AddSingleton<IAislePilotMealSwapPipeline, AislePilotMealSwapPipeline>();
        builder.Services.AddSingleton<AislePilotSlotSelectionEngine>();
        builder.Services.AddSingleton<AislePilotNutritionRecipeFallbackEngine>();
        builder.Services.AddSingleton<AislePilotPantryRankingEngine>();
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
        builder.Services.AddMyBlogRateLimiting();
    }

    private static Task RecordAuthEventAsync(
        HttpContext httpContext,
        string eventName,
        bool success,
        string? reason = null)
    {
        MyBlogTelemetry.RecordAuthEvent(eventName, success);
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("MyBlog.Authentication");

        var userIdHash = HashIdentifier(httpContext.User?.Identity?.Name);
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        logger.LogInformation(
            "Authentication event. EventName={EventName} Success={Success} Reason={Reason} UserIdHash={UserIdHash} Path={Path} TraceId={TraceId}",
            eventName,
            success,
            reason ?? string.Empty,
            userIdHash,
            path,
            traceId);
        return Task.CompletedTask;
    }

    private static string HashIdentifier(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue.Trim()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
