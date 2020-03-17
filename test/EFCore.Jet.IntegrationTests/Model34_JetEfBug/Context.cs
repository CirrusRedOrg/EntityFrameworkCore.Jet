using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model34_JetEfBug
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions options) : 
            base(options) { }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Item> Items { get; set; }
    }
}
