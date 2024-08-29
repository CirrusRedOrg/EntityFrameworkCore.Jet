using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model74_ComplexTypeContained_Github9536
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Friend> Friends { get; set; }
        public DbSet<LessThanFriend> LessThanFriends { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Friend>()
                .OwnsOne(_ => _.Address)
                .OwnsOne(_ => _.CityAddress1);
            modelBuilder.Entity<Friend>()
                .OwnsOne(_ => _.Address)
                .OwnsOne(_ => _.CityAddress2);
            modelBuilder.Entity<LessThanFriend>()
                .OwnsOne(_ => _.Address);
        }
    }
}
