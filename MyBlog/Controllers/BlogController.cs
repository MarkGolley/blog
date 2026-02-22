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
    
    [HttpGet("/blog/{slug}", Name = "blogPost")]
    public async Task<IActionResult> Post(string slug)
    {
        var post = _blogService.GetPostBySlug(slug);
        if (post == null) return NotFound();

        var comments = await _commentService.GetCommentsAsync(slug);
        return View(new BlogPostViewModel { Post = post, Comments = comments });
    }
    
    [HttpPost]
    public async Task<IActionResult> AddComment(Comment comment)
    {
        if (!ModelState.IsValid)
        {
            var post = _blogService.GetPostBySlug(comment.PostId);
            var comments = await _commentService.GetCommentsAsync(comment.PostId);
            var vm = new BlogPostViewModel { Post = post, Comments = comments };
            return View("Post", vm);
        }

        await _commentService.AddCommentAsync(comment);
        
        return RedirectToRoute("blogPost", new { slug = comment.PostId });
    }
}
