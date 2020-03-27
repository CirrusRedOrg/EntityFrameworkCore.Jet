using System.Data.Jet;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class DatabaseHandlingTest
    {
        [TestMethod]
        public void EnsureDeleted_Github21()
        {
            var storePath = DatabaseHandlingTestContext.GetStorePath();
            
            File.Delete(storePath);

            using (var ctx = new DatabaseHandlingTestContext())
            {
                ctx.Database.EnsureCreated();
            }

            Assert.IsTrue(File.Exists(storePath), "The db has not been created");

            using (var ctx = new DatabaseHandlingTestContext())
            {
                ctx.Database.EnsureDeleted();
            }

            Assert.IsFalse(File.Exists(storePath), "The db has not been deleted");
        }

        public class DatabaseHandlingTestContext : DbContext
        {
            public static string GetStorePath() => Helpers.GetJetStorePath(nameof(DatabaseHandlingTest) + ".accdb");

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseJet(JetConnection.GetConnectionString(GetStorePath(), JetConfiguration.DefaultProviderFactory), JetConfiguration.DefaultProviderFactory);
            }
        }
    }
}