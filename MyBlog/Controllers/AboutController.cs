using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public class AboutController : Controller
{
    public IActionResult About()
    {
        return View();
    }
}