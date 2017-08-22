using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model52_Requires_HasFalse_LogicalDelete
{
    public class Context : DbContext
    {
        // For migration test
        public Context()
        { }


        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        public DbSet<PanelLookup> PanelLookups { get; set; }
        public DbSet<PanelTexture> PanelTextures { get; set; }
        public DbSet<Texture> Textures { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Properties<int>().Where(p => p.Name == "Id").Configure(p => p.IsKey());
            modelBuilder.Entity<Texture>().Map(m => m.Requires("IsDeleted").HasValue(false)).Ignore(m => m.IsDeleted);


            modelBuilder.Entity<PanelTexture>().HasKey(e => new { e.PanelId, e.TextureId, e.IsInterior });
            modelBuilder.Entity<PanelTexture>().HasOne(e => e.Texture).WithMany(e => e.PanelTextures).HasForeignKey(e => e.TextureId);
            modelBuilder.Entity<PanelTexture>().HasOne(e => e.PanelLookup).WithMany(e => e.PanelTextures).HasForeignKey(e => e.PanelId);
        }
    }
}
