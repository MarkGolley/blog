using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public class BlogController : Controller
{
    private readonly BlogService _blogService;
    private readonly CommentService _commentService;

    public BlogController(BlogService blogService, CommentService commentService)
    {
        _blogService = blogService;
        _commentService = commentService;
    }

    public IActionResult Index()
    {
        var posts = _blogService.GetAllPosts();
        return View(posts);
    }

    [HttpGet("{slug}")]
    public IActionResult Post(string slug)
    {
        var post = _blogService.GetPostBySlug(slug);
        var comments = _commentService.GetComments(slug);
        if (post == null) return NotFound();
        return View(new BlogPostViewModel { Post = post, Comments = comments });
    }
    
    [HttpPost("comment")]
    public IActionResult PostComment(Comment comment)
    {
        _commentService.AddComment(comment);
        return RedirectToAction("Post", new { slug = comment.Id });
    }
}
