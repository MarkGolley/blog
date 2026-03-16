using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

[Route("projects")]
public class ProjectsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}
