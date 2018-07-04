using System;
using System.IO;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    [TestClass]
    public class DatabaseHandlingTest
    {
        [TestMethod]
        public void EnsureDeleted_Github21()
        {
            File.Delete(DatabaseHandlingTestContext.GetDbPath());

            using (var ctx = new DatabaseHandlingTestContext())
            {
                ctx.Database.EnsureCreated();
            }

            Assert.IsTrue(File.Exists(DatabaseHandlingTestContext.GetDbPath()), "The db has not been created");

            using (var ctx = new DatabaseHandlingTestContext())
            {
                ctx.Database.EnsureDeleted();
            }

            Assert.IsFalse(File.Exists(DatabaseHandlingTestContext.GetDbPath()), "The db has not been deleted");

        }
        public class DatabaseHandlingTestContext : DbContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseJet($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={GetDbPath()};");
            }

            public static string GetDbPath()
            {
                return Path.Combine(Helpers.GetTestDirectory(), "db.mdb");
            }
        }
    }
}
