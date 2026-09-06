using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    [HttpGet("rate-limited")]
    public IActionResult RateLimited(string? returnUrl = null)
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ModelState.AddModelError(
            string.Empty,
            "Too many requests. Wait a moment, then generate your plan again.");

        var request = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });
        return View("Index", BuildPageModel(request, returnUrl: ResolveReturnUrl(returnUrl)));
    }
}
