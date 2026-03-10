using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public class SubscriptionRequestModel
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    public string? ReturnPath { get; set; }
}
