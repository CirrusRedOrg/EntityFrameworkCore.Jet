using System;
using System.Data.Jet;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class Issue305Test
    {
        [Fact]
        public void Issue305_Test()
        {
            using (var db = new TiffFilesContext())
            {
                db.Database.EnsureDeleted();

                db.Database.Migrate();
            }
        }

        public class TiffFilesContext : DbContext
        {
            public DbSet<FileInfo> Files { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseJet(JetConnection.GetConnectionString("Issue305Database.accdb"));
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<FileInfo>().Property(f => f.Path).IsRequired();
            }
        }

        public class FileInfo
        {
            public int FileInfoId { get; set; }
            public string Path { get; set; }
            public String BlindedName { get; set; }
            public bool ContainsSynapse { get; set; }
            public int Quality { get; set; }
        }
    }
}
