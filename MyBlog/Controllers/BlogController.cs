using Microsoft.AspNetCore.Mvc;
using MailKit.Net.Smtp;
using MimeKit;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public class BlogController : Controller
{
    private readonly BlogService _blogService;
    private readonly CommentService _commentService;
    private readonly ILogger<BlogController> _logger;

    public BlogController(BlogService blogService, CommentService commentService, ILogger<BlogController> logger)
    {
        _blogService = blogService;
        _commentService = commentService;
        _logger = logger;
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
        await SendNewCommentAlertAsync(comment);
        
        return RedirectToRoute("blogPost", new { slug = comment.PostId });
    }

    private async Task SendNewCommentAlertAsync(Comment comment)
    {
        try
        {
            var emailAddress = Environment.GetEnvironmentVariable("ICLOUD_EMAIL");
            var emailPassword = Environment.GetEnvironmentVariable("ICLOUD_APP_PASSWORD");

            if (string.IsNullOrWhiteSpace(emailAddress) || string.IsNullOrWhiteSpace(emailPassword))
            {
                _logger.LogWarning("Comment alert email not sent because email credentials are not configured.");
                return;
            }

            var postUrl = Url.RouteUrl("blogPost", new { slug = comment.PostId }, Request.Scheme)
                         ?? comment.PostId;

            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress("MyBlog", emailAddress));
            emailMessage.To.Add(new MailboxAddress("Mark Golley", emailAddress));
            emailMessage.Subject = $"New comment on blog post: {comment.PostId}";
            emailMessage.Body = new TextPart("plain")
            {
                Text =
                    $"A new comment was posted.\n\nPost: {comment.PostId}\nAuthor: {comment.Author}\nPosted (UTC): {comment.PostedAt:u}\n\nComment:\n{comment.Content}\n\nView post: {postUrl}"
            };

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.mail.me.com", 587, false);
            await client.AuthenticateAsync(emailAddress, emailPassword);
            await client.SendAsync(emailMessage);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending new comment alert email for post {PostId}.", comment.PostId);
        }
    }
}
