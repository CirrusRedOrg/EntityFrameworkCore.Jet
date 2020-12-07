using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Jet
{
    public class JetMigrationTest : TestBase<JetMigrationTest.Context>
    {
        [ConditionalFact]
        public virtual void Create_table_with_HasDefaultValueSql()
        {
            using var context = CreateContext(
                builder =>
                {
                    builder.Entity<Cookie>(
                        entity =>
                        {
                            entity.Property(e => e.BestServedBefore)
                                .HasDefaultValueSql("#2021-12-31#");

                            entity.HasData(
                                new Cookie
                                {
                                    CookieId = 1,
                                    Name = "Basic",
                                });
                        });
                });
            
            var cookies = context.Set<Cookie>()
                .ToList();
            
            Assert.Single(cookies);
            Assert.Equal(new DateTime(2021, 12, 31), cookies[0].BestServedBefore);
        }

        public class Cookie
        {
            public int CookieId { get; set; }
            public string Name { get; set; }
            public DateTime BestServedBefore { get; set; }
        }

        public class Context : ContextBase
        {
        }
    }
}