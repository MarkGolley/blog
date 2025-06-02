using System.Text.Json;
using MyBlog.Models;

namespace MyBlog.Services;

public class CommentService
{
    private readonly string _basePath = "App_Data/comments";
    
    public void AddComment(Comment comment)
    {
        var comments = GetComments(comment.Id.ToString());
        comment.PostedAt = DateTime.Now;
        comment.Id = comments.Count + 1;
        comments.Add(comment);
        var json = JsonSerializer.Serialize(comments, options: new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(_basePath);
        File.WriteAllText(Path.Combine(_basePath, $"{comment.Id}.json"), json);
    }
    
    public List<Comment> GetComments(string postId)
    {
        var path = Path.Combine(_basePath, $"{postId}.json");
        if (!File.Exists(path)) return [];
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Comment>>(json) ?? [];
    }
}
