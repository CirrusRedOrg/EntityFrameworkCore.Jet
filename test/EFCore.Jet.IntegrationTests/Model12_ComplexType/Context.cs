using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model12_ComplexType
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options) { }

        public DbSet<Friend> Friends { get; set; }
        public DbSet<LessThanFriend> LessThanFriends { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Friend>()
                .OwnsOne(_ => _.Address);
            modelBuilder.Entity<LessThanFriend>()
                .OwnsOne(_ => _.Address);
        }
    }
}
