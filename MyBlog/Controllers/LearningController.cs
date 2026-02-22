using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

public class LearningController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}