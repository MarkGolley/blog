using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MailKit.Net.Smtp;
using MimeKit;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public class BlogController : Controller
{
    private const string VisitorIdCookieName = "myblog_visitor_id";
    private const int MaxPinnedPosts = 3;

    private readonly BlogService _blogService;
    private readonly CommentService _commentService;
    private readonly LikeService _likeService;
    private readonly ILogger<BlogController> _logger;

    public BlogController(
        BlogService blogService,
        CommentService commentService,
        LikeService likeService,
        ILogger<BlogController> logger)
    {
        _blogService = blogService;
        _commentService = commentService;
        _likeService = likeService;
        _logger = logger;
    }
    
    public async Task<IActionResult> Index()
    {
        var visitorId = GetOrCreateVisitorId();
        var posts = _blogService.GetAllPosts().ToList();
        var summaries = await _likeService.GetPostLikeSummariesAsync(posts.Select(x => x.Id), visitorId);

        var postItems = posts.Select(post =>
        {
            summaries.TryGetValue(post.Id, out var summary);
            return new BlogListItemViewModel
            {
                Post = post,
                LikeCount = summary.Count,
                IsLikedByCurrentVisitor = summary.IsLikedByVisitor
            };
        }).ToList();

        var pinnedPosts = postItems
            .Where(x => x.LikeCount > 0)
            .OrderByDescending(x => x.LikeCount)
            .ThenByDescending(x => x.Post.DatePosted)
            .Take(MaxPinnedPosts)
            .ToList();

        var pinnedIds = pinnedPosts
            .Select(x => x.Post.Id)
            .ToHashSet(StringComparer.Ordinal);

        var vm = new BlogIndexViewModel
        {
            PinnedPosts = pinnedPosts,
            Posts = postItems.Where(x => !pinnedIds.Contains(x.Post.Id)).ToList()
        };

        return View(vm);
    }
    
    [HttpGet("/blog/{slug}", Name = "blogPost")]
    public async Task<IActionResult> Post(string slug)
    {
        var post = _blogService.GetPostBySlug(slug);
        if (post == null) return NotFound();

        var viewModel = await BuildPostViewModelAsync(post);
        return View(viewModel);
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("commentWrites")]
    public async Task<IActionResult> AddComment(Comment comment)
    {
        var honeypot = Request.Form["__hp"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(honeypot))
        {
            _logger.LogWarning("Comment blocked by honeypot spam check for post {PostId}.", comment.PostId);

            if (string.IsNullOrWhiteSpace(comment.PostId))
            {
                return RedirectToAction(nameof(Index));
            }

            var spamPostUrl = Url.RouteUrl("blogPost", new { slug = comment.PostId }) ?? $"/blog/{comment.PostId}";
            return Redirect(spamPostUrl);
        }

        if (!ModelState.IsValid)
        {
            var post = _blogService.GetPostBySlug(comment.PostId);
            if (post == null) return NotFound();
            var vm = await BuildPostViewModelAsync(post);
            return View("Post", vm);
        }

        try
        {
            await _commentService.AddCommentAsync(comment);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var post = _blogService.GetPostBySlug(comment.PostId);
            if (post == null) return NotFound();
            var vm = await BuildPostViewModelAsync(post);
            return View("Post", vm);
        }

        await SendNewCommentAlertAsync(comment);
        
        var postUrl = Url.RouteUrl("blogPost", new { slug = comment.PostId }) ?? $"/blog/{comment.PostId}";
        return Redirect($"{postUrl}#comment-{comment.Id}");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("likeWrites")]
    public async Task<IActionResult> TogglePostLike(string postId, string? returnSlug, string? returnTo, string? returnAnchor)
    {
        if (string.IsNullOrWhiteSpace(postId))
        {
            return NotFound();
        }

        var post = _blogService.GetPostBySlug(postId);
        if (post == null)
        {
            return NotFound();
        }

        var visitorId = GetOrCreateVisitorId();
        await _likeService.TogglePostLikeAsync(postId, visitorId);

        if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            var summary = await _likeService.GetPostLikeSummaryAsync(postId, visitorId);
            return Json(new
            {
                success = true,
                count = summary.Count,
                isLiked = summary.IsLikedByVisitor
            });
        }

        if (string.Equals(returnTo, "index", StringComparison.OrdinalIgnoreCase))
        {
            var blogIndexUrl = Url.RouteUrl("blogIndex") ?? "/blog";
            var indexAnchor = string.IsNullOrWhiteSpace(returnAnchor) ? $"post-{postId}" : returnAnchor;
            return Redirect($"{blogIndexUrl}#{indexAnchor}");
        }

        var postUrl = Url.RouteUrl("blogPost", new { slug = returnSlug ?? postId }) ?? $"/blog/{returnSlug ?? postId}";
        if (!string.IsNullOrWhiteSpace(returnAnchor))
        {
            return Redirect($"{postUrl}#{returnAnchor}");
        }

        return Redirect(postUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("likeWrites")]
    public async Task<IActionResult> ToggleCommentLike(int commentId, string postId, string? returnSlug, string? returnAnchor)
    {
        if (commentId <= 0 || string.IsNullOrWhiteSpace(postId))
        {
            return NotFound();
        }

        var post = _blogService.GetPostBySlug(postId);
        if (post == null)
        {
            return NotFound();
        }

        var visitorId = GetOrCreateVisitorId();
        var updated = await _likeService.ToggleCommentLikeAsync(commentId, postId, visitorId);
        if (!updated)
        {
            return NotFound();
        }

        var baseUrl = Url.RouteUrl("blogPost", new { slug = returnSlug ?? postId }) ?? $"/blog/{returnSlug ?? postId}";
        var anchor = string.IsNullOrWhiteSpace(returnAnchor) ? $"comment-{commentId}" : returnAnchor;
        return Redirect($"{baseUrl}#{anchor}");
    }

    private async Task<BlogPostViewModel> BuildPostViewModelAsync(BlogPost post)
    {
        var visitorId = GetOrCreateVisitorId();
        var comments = await _commentService.GetCommentsAsync(post.Id);
        var commentIds = FlattenCommentIds(comments);
        var commentLikeSummaries = await _likeService.GetCommentLikeSummariesAsync(commentIds, visitorId);
        ApplyLikeSummaries(comments, commentLikeSummaries);

        var postLikeSummary = await _likeService.GetPostLikeSummaryAsync(post.Id, visitorId);

        return new BlogPostViewModel
        {
            Post = post,
            Comments = comments,
            PostLikeCount = postLikeSummary.Count,
            IsPostLikedByCurrentVisitor = postLikeSummary.IsLikedByVisitor
        };
    }

    private static List<int> FlattenCommentIds(IEnumerable<CommentThreadViewModel> comments)
    {
        var ids = new List<int>();
        foreach (var comment in comments)
        {
            ids.Add(comment.Comment.Id);
            ids.AddRange(FlattenCommentIds(comment.Replies));
        }

        return ids;
    }

    private static void ApplyLikeSummaries(
        IEnumerable<CommentThreadViewModel> comments,
        IReadOnlyDictionary<int, (int Count, bool IsLikedByVisitor)> summaries)
    {
        foreach (var comment in comments)
        {
            if (summaries.TryGetValue(comment.Comment.Id, out var summary))
            {
                comment.LikeCount = summary.Count;
                comment.IsLikedByCurrentVisitor = summary.IsLikedByVisitor;
            }
            else
            {
                comment.LikeCount = 0;
                comment.IsLikedByCurrentVisitor = false;
            }

            ApplyLikeSummaries(comment.Replies, summaries);
        }
    }

    private string GetOrCreateVisitorId()
    {
        if (Request.Cookies.TryGetValue(VisitorIdCookieName, out var existingVisitorId) &&
            !string.IsNullOrWhiteSpace(existingVisitorId))
        {
            return existingVisitorId;
        }

        var visitorId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(
            VisitorIdCookieName,
            visitorId,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(2),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });

        return visitorId;
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
