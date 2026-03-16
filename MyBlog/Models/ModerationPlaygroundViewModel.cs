using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public class ModerationPlaygroundViewModel
{
    [Required(ErrorMessage = "Enter some text to evaluate.")]
    [StringLength(2000, MinimumLength = 3, ErrorMessage = "Text must be between 3 and 2000 characters.")]
    public string InputText { get; set; } = string.Empty;

    public bool HasResult { get; set; }
    public bool? IsSafe { get; set; }
    public long? LatencyMs { get; set; }
    public string ResultTitle { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
}
