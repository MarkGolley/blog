using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

[Route("projects/word-garden")]
public class WordGardenController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
