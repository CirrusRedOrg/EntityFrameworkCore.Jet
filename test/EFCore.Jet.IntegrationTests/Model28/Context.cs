using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model28
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Advertisement> Advertisements { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<AdImage> AdImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasMany(u => u.Advertisements)
                .WithOne(x => x.User)
                .IsRequired()
                ;

            modelBuilder.Entity<Advertisement>()
                .HasMany(a => a.AdImages)
                .WithOne(x => x.Advertisement)
                .IsRequired()
                ;

            // Is required must be inserted in foreign key field if there is one
            /*
            modelBuilder.Entity<AdImage>()
                .Property(x => x.Advertisement)
                .IsRequired()
                ;
            */

            base.OnModelCreating(modelBuilder);

        }
    }
}
