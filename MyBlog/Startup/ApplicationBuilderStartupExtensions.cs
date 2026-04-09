using Microsoft.Extensions.FileProviders;

namespace MyBlog.Startup;

internal static class ApplicationBuilderStartupExtensions
{
    public static WebApplication ConfigureMyBlogPipeline(
        this WebApplication app,
        AppMode appMode,
        string appVersion)
    {
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

                var cachePolicy = AppRequestPolicies.ResolveCachePolicy(context);
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
            if (appMode == AppMode.Combined || AppRequestPolicies.IsRequestAllowedInMode(context.Request.Path, appMode))
            {
                await next();
                return;
            }

            if (appMode == AppMode.AislePilotOnly && AppRequestPolicies.IsRootPath(context.Request.Path))
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
            if (!AppRequestPolicies.ShouldRecoverFromBadRequest(requestPath))
            {
                return;
            }

            var returnPath = AppRequestPolicies.ResolveBadRequestReturnPath(httpContext.Request, requestPath);
            if (string.IsNullOrWhiteSpace(returnPath))
            {
                return;
            }

            var returnPathWithFlag = AppRequestPolicies.AddOrReplaceQueryParameter(returnPath, "form", "expired");
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
            httpContext.Response.Headers.Location = returnPathWithFlag;
        });

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

        return app;
    }
}
