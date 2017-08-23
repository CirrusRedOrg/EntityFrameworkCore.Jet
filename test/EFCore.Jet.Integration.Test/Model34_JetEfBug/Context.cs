using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model34_JetEfBug
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions options) : 
            base(options) { }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Item> Items { get; set; }
    }
}
