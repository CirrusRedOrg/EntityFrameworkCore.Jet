using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model08
{
    public class Context : DbContext
    {
        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<File> Files { get; set; }
        public DbSet<GalleryImage> GalleryImages { get; set; }
        public DbSet<PageImage> PageImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new FileMap());
            modelBuilder.ApplyConfiguration(new GalleryImageMap());
            modelBuilder.ApplyConfiguration(new PageImageMap());
        }
    }


}
