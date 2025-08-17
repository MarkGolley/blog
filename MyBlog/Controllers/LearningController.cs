using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

public class LearningController : Controller
{
    public IActionResult Learning()
    {
        return View();
    }
}