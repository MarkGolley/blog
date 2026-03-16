using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;
using System.Security.Claims;

namespace MyBlog.Controllers;

public class AdminController : Controller
{
    private readonly CommentService _commentService;

    public AdminController(CommentService commentService)
    {
        _commentService = commentService;
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login(string username, string password)
    {
        // Simple check - in production, use proper auth
        var adminUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME") ?? "admin";
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "password";
        var normalizedUsername = username?.Trim() ?? string.Empty;

        if (string.Equals(normalizedUsername, adminUsername, StringComparison.OrdinalIgnoreCase) &&
            password == adminPassword)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, normalizedUsername),
                new Claim(ClaimTypes.Role, "Admin")
            };

            var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity), authProperties);
            return await RenderDashboardAsync();
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("CookieAuth");
        return RedirectToAction("Index", "Blog");
    }

    [Authorize]
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index()
    {
        var pendingComments = await _commentService.GetPendingCommentsAsync();
        return View(pendingComments);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int commentId)
    {
        await _commentService.ApproveCommentAsync(commentId);
        return await RenderDashboardAsync();
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int commentId)
    {
        await _commentService.DeleteCommentAsync(commentId);
        return await RenderDashboardAsync();
    }

    private async Task<IActionResult> RenderDashboardAsync()
    {
        var pendingComments = await _commentService.GetPendingCommentsAsync();
        return View("Index", pendingComments);
    }
}
