using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

[Route("ai-experiments")]
public class AIExperimentsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}
