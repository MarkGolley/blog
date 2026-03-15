using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    [ValidateAntiForgeryToken]
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

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity), authProperties);

            return RedirectToAction("Index");
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
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index()
    {
        var pendingComments = await _commentService.GetPendingCommentsAsync();
        return View(pendingComments);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int commentId, string returnUrl)
    {
        await _commentService.ApproveCommentAsync(commentId);
        return Redirect(returnUrl ?? "/Admin");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int commentId, string returnUrl)
    {
        await _commentService.DeleteCommentAsync(commentId);
        return Redirect(returnUrl ?? "/Admin");
    }
}
