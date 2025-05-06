using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;

namespace MyBlog.Controllers;

public class BlogController : Controller
{
    private readonly BlogService _blogService;

    public BlogController(BlogService blogService)
    {
        _blogService = blogService;
    }

    public IActionResult Index()
    {
        var posts = _blogService.GetAllPosts();
        return View(posts);
    }

    public IActionResult Post(string slug)
    {
        var post = _blogService.GetPostBySlug(slug);
        if (post == null) return NotFound();
        return View(post);
    }
}
