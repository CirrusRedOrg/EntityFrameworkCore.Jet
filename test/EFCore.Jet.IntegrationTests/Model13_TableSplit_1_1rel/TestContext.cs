using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model13_TableSplit_1_1rel
{
    public class TestContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Address> Adresses { get; set; }
        public DbSet<Visit> Visits { get; set; }

    }
}
