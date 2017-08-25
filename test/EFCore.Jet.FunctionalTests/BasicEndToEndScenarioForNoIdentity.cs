using System.Linq;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class BasicEndToEndScenarioForNoIdentity
    {
        [Fact]
        public void Can_run_end_to_end_scenario()
        {
            using (var db = new BloggingContext())
            {
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
                db.Blogs.Add(new Blog { Id = 99, Url = "http://www.bubi.it" });
                db.SaveChanges();

                var blogs = db.Blogs.ToList();

                Assert.Equal(blogs.Count, 1);
                Assert.Equal(blogs[0].Url, "http://www.bubi.it");
                Assert.Equal(blogs[0].Id, 99);
            }
        }

        public class BloggingContext : DbContext
        {
            public DbSet<Blog> Blogs { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseJet(@"Data Source=BloggingNoIdentity.sdf");
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
                modelBuilder.Entity<Blog>()
                    .Property(e => e.Id)
                    .ValueGeneratedNever();
            }
        }

        public class Blog
        {
            public int Id { get; set; }
            public string Url { get; set; }
        }
    }
}
