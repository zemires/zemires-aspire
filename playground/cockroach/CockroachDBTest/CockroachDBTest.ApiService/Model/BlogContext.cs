using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CockroachDBTest.ApiService.Model;

public class Blog
{
    public int BlogId { get; set; }
    public string Url { get; set; } = string.Empty;

    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int PostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }

    public BloggingContext(DbContextOptions<BloggingContext> options)
        : base(options)
    {
    }
}