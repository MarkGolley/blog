using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public class AdminNotifySubscribersRequestModel
{
    [Required]
    [StringLength(200)]
    public string PostSlug { get; set; } = string.Empty;

    public string? AdminKey { get; set; }
}
