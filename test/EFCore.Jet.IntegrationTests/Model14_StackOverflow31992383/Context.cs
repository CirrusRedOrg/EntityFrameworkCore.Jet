using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model14_StackOverflow31992383
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options) { }

        public DbSet<Class1> C1s { get; set; }
        public DbSet<Class3> C3s { get; set; }


    }
}
