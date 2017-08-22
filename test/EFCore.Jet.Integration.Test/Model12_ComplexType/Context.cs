using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model12_ComplexType
{
    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options) { }

        public DbSet<Friend> Friends { get; set; }
        public DbSet<LessThanFriend> LessThanFriends { get; set; }
    }
}
