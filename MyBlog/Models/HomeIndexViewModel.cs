namespace MyBlog.Models;

public sealed class HomeIndexViewModel
{
    public DailyCodingCapsuleViewModel DailyCapsule { get; init; } = new();
}
