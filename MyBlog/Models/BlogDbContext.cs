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
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<CommentLike> CommentLikes => Set<CommentLike>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PostLike>()
            .HasIndex(x => new { x.PostId, x.VisitorId })
            .IsUnique();

        modelBuilder.Entity<CommentLike>()
            .HasIndex(x => new { x.CommentId, x.VisitorId })
            .IsUnique();

        modelBuilder.Entity<CommentLike>()
            .HasOne(x => x.Comment)
            .WithMany(c => c.Likes)
            .HasForeignKey(x => x.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
