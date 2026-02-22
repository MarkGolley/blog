using Microsoft.EntityFrameworkCore;
using MyBlog.Models;

namespace MyBlog.Data;

public class BlogDbContext : DbContext
{
    public BlogDbContext(DbContextOptions<BlogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Comment> Comments => Set<Comment>();
}