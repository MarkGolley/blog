using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

[Route("subscribe")]
public class SubscriptionController : Controller
{
    private readonly SubscriptionService _subscriptionService;
    private readonly SubscriptionEmailService _subscriptionEmailService;
    private readonly BlogService _blogService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        SubscriptionService subscriptionService,
        SubscriptionEmailService subscriptionEmailService,
        BlogService blogService,
        IConfiguration configuration,
        ILogger<SubscriptionController> logger)
    {
        _subscriptionService = subscriptionService;
        _subscriptionEmailService = subscriptionEmailService;
        _blogService = blogService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("subscriptionWrites")]
    public async Task<IActionResult> Subscribe(
        SubscriptionRequestModel model,
        [FromForm(Name = "__hp")] string? honeypot)
    {
        var returnPath = GetSafeReturnPath(model.ReturnPath);

        if (!string.IsNullOrWhiteSpace(honeypot))
        {
            TempData["SubscribeBanner"] = "Check your email to confirm your subscription.";
            return Redirect(returnPath);
        }

        if (!ModelState.IsValid)
        {
            TempData["SubscribeBanner"] = "Enter a valid email address to subscribe.";
            return Redirect(returnPath);
        }

        try
        {
            var result = await _subscriptionService.SubscribeAsync(model.Email);

            if (result.Status == SubscribeStatus.AlreadySubscribed)
            {
                TempData["SubscribeBanner"] = "You're already subscribed.";
                return Redirect(returnPath);
            }

            if (string.IsNullOrWhiteSpace(result.ConfirmationToken) || string.IsNullOrWhiteSpace(result.UnsubscribeToken))
            {
                TempData["SubscribeBanner"] = "Unable to complete subscription right now.";
                return Redirect(returnPath);
            }

            var confirmationPath = Url.Action(
                action: nameof(Confirm),
                controller: "Subscription",
                values: new { token = result.ConfirmationToken }) ?? "/subscribe/confirm";
            var unsubscribePath = Url.Action(
                action: nameof(Unsubscribe),
                controller: "Subscription",
                values: new { token = result.UnsubscribeToken }) ?? "/subscribe/unsubscribe";

            var confirmationUrl = BuildAbsoluteUrl(confirmationPath);
            var unsubscribeUrl = BuildAbsoluteUrl(unsubscribePath);

            var emailSent = await _subscriptionEmailService.SendConfirmationEmailAsync(
                result.Email,
                confirmationUrl,
                unsubscribeUrl);

            TempData["SubscribeBanner"] = emailSent
                ? "Check your email to confirm your subscription."
                : "Subscription saved, but confirmation email could not be sent.";

            return Redirect(returnPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription request failed.");
            TempData["SubscribeBanner"] = "Unable to subscribe right now. Please try again shortly.";
            return Redirect(returnPath);
        }
    }

    [HttpGet("confirm")]
    public async Task<IActionResult> Confirm(string? token)
    {
        var result = await _subscriptionService.ConfirmAsync(token ?? string.Empty);
        var vm = result.Status switch
        {
            SubscriptionTokenStatus.Completed => new SubscriptionStatusViewModel
            {
                IsSuccess = true,
                Title = "Subscription Confirmed",
                Message = "Thanks, you're now subscribed to new-post notifications."
            },
            SubscriptionTokenStatus.AlreadyApplied => new SubscriptionStatusViewModel
            {
                IsSuccess = true,
                Title = "Already Confirmed",
                Message = "This subscription was already confirmed."
            },
            _ => new SubscriptionStatusViewModel
            {
                IsSuccess = false,
                Title = "Invalid Link",
                Message = "This confirmation link is invalid or expired."
            }
        };

        return View("Status", vm);
    }

    [HttpGet("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(string? token)
    {
        var result = await _subscriptionService.UnsubscribeAsync(token ?? string.Empty);
        var vm = result.Status switch
        {
            SubscriptionTokenStatus.Completed => new SubscriptionStatusViewModel
            {
                IsSuccess = true,
                Title = "Unsubscribed",
                Message = "You have been unsubscribed from new-post notifications."
            },
            SubscriptionTokenStatus.AlreadyApplied => new SubscriptionStatusViewModel
            {
                IsSuccess = true,
                Title = "Already Unsubscribed",
                Message = "You were already unsubscribed from this list."
            },
            _ => new SubscriptionStatusViewModel
            {
                IsSuccess = false,
                Title = "Invalid Link",
                Message = "This unsubscribe link is invalid."
            }
        };

        return View("Status", vm);
    }

    [HttpPost("/admin/subscribers/notify-post")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> NotifyPost(AdminNotifySubscribersRequestModel request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, error = "postSlug is required." });
        }

        var configuredAdminKey = Environment.GetEnvironmentVariable("SUBSCRIBER_NOTIFY_KEY")
                                 ?? _configuration["Subscriptions:NotifyAdminKey"];

        if (string.IsNullOrWhiteSpace(configuredAdminKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                error = "Subscriber notification key is not configured."
            });
        }

        var providedAdminKey = Request.Headers["X-Admin-Key"].FirstOrDefault() ?? request.AdminKey ?? string.Empty;
        if (!KeysMatch(configuredAdminKey, providedAdminKey))
        {
            return Unauthorized(new { success = false, error = "Unauthorized." });
        }

        var post = _blogService.GetPostBySlug(request.PostSlug);
        if (post == null)
        {
            return NotFound(new { success = false, error = "Post not found." });
        }

        var postPath = Url.RouteUrl("blogPost", new { slug = post.Id })
                       ?? $"/blog/{Uri.EscapeDataString(post.Id)}";
        var postUrl = BuildAbsoluteUrl(postPath);

        var subscribers = await _subscriptionService.GetConfirmedSubscribersAsync();

        var sent = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var subscriber in subscribers)
        {
            if (await _subscriptionService.HasPostNotificationAsync(subscriber.SubscriberId, post.Id))
            {
                skipped++;
                continue;
            }

            var unsubscribePath = Url.Action(
                action: nameof(Unsubscribe),
                controller: "Subscription",
                values: new { token = subscriber.UnsubscribeToken }) ?? "/subscribe/unsubscribe";
            var unsubscribeUrl = BuildAbsoluteUrl(unsubscribePath);

            var emailSent = await _subscriptionEmailService.SendNewPostNotificationAsync(
                subscriber.Email,
                post.Title,
                postUrl,
                unsubscribeUrl);

            if (!emailSent)
            {
                failed++;
                continue;
            }

            await _subscriptionService.MarkPostNotificationAsync(subscriber.SubscriberId, post.Id);
            sent++;
        }

        _logger.LogInformation(
            "Subscriber notification for post {PostId} completed. Sent: {Sent}, Skipped: {Skipped}, Failed: {Failed}.",
            post.Id,
            sent,
            skipped,
            failed);

        return Content(JsonSerializer.Serialize(new
        {
            success = true,
            postId = post.Id,
            sent,
            skipped,
            failed
        }), "application/json");
    }

    private string GetSafeReturnPath(string? returnPath)
    {
        if (!string.IsNullOrWhiteSpace(returnPath) && Url.IsLocalUrl(returnPath))
        {
            return returnPath;
        }

        return Url.RouteUrl("blogIndex") ?? "/blog";
    }

    private static bool KeysMatch(string expected, string provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private string BuildAbsoluteUrl(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absoluteUri) &&
            IsHttpUri(absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var relativePath = pathOrUrl.StartsWith("/", StringComparison.Ordinal)
            ? pathOrUrl
            : "/" + pathOrUrl;

        return $"{GetPublicBaseUrl()}{relativePath}";
    }

    private string GetPublicBaseUrl()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
                                ?? _configuration["Site:PublicBaseUrl"];

        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            var trimmedConfiguredUrl = configuredBaseUrl.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmedConfiguredUrl, UriKind.Absolute, out var configuredUri) &&
                IsHttpUri(configuredUri))
            {
                return trimmedConfiguredUrl;
            }
        }

        var forwardedProto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
            ?.Split(',')[0]
            .Trim();
        var forwardedHost = Request.Headers["X-Forwarded-Host"].FirstOrDefault()
            ?.Split(',')[0]
            .Trim();

        var scheme = string.Equals(forwardedProto, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(forwardedProto, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? forwardedProto!
            : Request.Scheme;

        if (!string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            scheme = Uri.UriSchemeHttps;
        }

        var host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost
            : Request.Host.Value;

        if (!string.IsNullOrWhiteSpace(host))
        {
            return $"{scheme}://{host}".TrimEnd('/');
        }

        // Last-resort fallback for environments where request host/scheme cannot be inferred.
        return "https://markgolley.dev";
    }

    private static bool IsHttpUri(Uri uri)
    {
        return uri.IsAbsoluteUri &&
               (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }
}
