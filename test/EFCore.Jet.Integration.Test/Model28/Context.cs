using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model28
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

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

            modelBuilder.Entity<AdImage>()
                .Property(x => x.Advertisement)
                .IsRequired()
                ;

            base.OnModelCreating(modelBuilder);

        }
    }
}
