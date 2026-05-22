namespace MyBlog.Models;

public class BlogPostQuiz
{
    public string Title { get; set; } = "Quick quiz";
    public List<BlogPostQuizQuestion> Questions { get; set; } = new();
}

public class BlogPostQuizQuestion
{
    public string Id { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int CorrectOptionIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
