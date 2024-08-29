using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model15_SortInheritedColumns
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Brand> Brands { get; set; }

    }
}
