using System.Collections.Generic;
using System.Data.Jet;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class Issue398Test
    {
        [Fact]
        public void Issue398_Test()
        {
            using (var db = new BloggingContext())
            {
                db.Database.EnsureDeleted();
                db.Database.Migrate();
            }
        }

        public class BloggingContext : DbContext
        {
            public DbSet<Blog> Blogs { get; set; }
            public DbSet<Post> Posts { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder dbContextOptionsBuilder)
            {
                dbContextOptionsBuilder.UseJet(JetConnection.GetConnectionString("Issue398Database.accdb"));
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Blog>()
                        .HasMany(b => b.Posts)
                        .WithOne(p => p.Blog)
                        .HasForeignKey(p => p.BlogId);
                modelBuilder.Entity<Post>()
                    .HasIndex(p => p.BlogId)
                    .HasName("NewIndex");
            }
        }

        public class Blog
        {
            public int BlogId { get; set; }
            public string Url { get; set; }

            public List<Post> Posts { get; set; }
        }

        public class Post
        {
            public int PostId { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }

            public int BlogId { get; set; }
            public Blog Blog { get; set; }
        }
    }
}
