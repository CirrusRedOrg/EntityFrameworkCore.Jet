// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet
{
    public class JetDateTimeTest : TestBase<JetDateTimeTest.Context>
    {
        [ConditionalFact]
        public virtual void SanityCheck()
        {
            using var context = CreateContext();
            context.Database.EnsureCreatedResiliently();

            var cookies = context.Cookies
                .ToList();
            
            Assert.Equal(2, cookies.Count);
        }

        [ConditionalFact]
        public virtual void Where_datetime_with_HasDefaultValue()
        {
            using var context = CreateContext();
            context.Database.EnsureCreatedResiliently();

            var cookies = context.Cookies
                .Where(o => o.BestServedBeforeDateTime == new DateTime(2021, 12, 31, 13, 42, 21, 123))
                .ToList();
            
            Assert.Equal(2, cookies.Count);
            Assert.True(cookies.All(c => c.BestServedBeforeDateTime == new DateTime(2021, 12, 31, 13, 42, 21, 123)));
        }

        [ConditionalFact]
        public virtual void Where_datetime_with_HasDefaultValue_precision()
        {
            using var context = CreateContext();
            context.Database.EnsureCreatedResiliently();

            var cookies = context.Cookies
                .Where(o => o.BestServedBeforeDateTime == new DateTime(2021, 12, 31, 13, 42, 21, 124))
                .ToList();
            
            Assert.Empty(cookies);
        }

        [ConditionalFact]
        public virtual void Where_datetime_with_HasDefaultValueSql()
        {
            using var context = CreateContext();
            context.Database.EnsureCreatedResiliently();

            var cookies = context.Cookies
                .Where(o => o.BestServedAfterDateTime == new DateTime(2020, 12, 31, 13, 42, 21))
                .ToList();
            
            Assert.Equal(2, cookies.Count);
            Assert.True(cookies.All(c => c.BestServedAfterDateTime == new DateTime(2020, 12, 31, 13, 42, 21)));
        }

        [ConditionalFact]
        public virtual void Where_datetime_with_fractions()
        {
            using var context = CreateContext();
            context.Database.EnsureCreatedResiliently();

            var cookies = context.Cookies
                .Where(o => o.PuchasedDateTime == new DateTime(2019, 12, 31, 13, 42, 21, 123))
                .ToList();
            
            Assert.Single(cookies);
            Assert.Equal(new DateTime(2019, 12, 31, 13, 42, 21, 123), cookies[0].PuchasedDateTime);
        }
        
        [ConditionalFact]
        public virtual void Where_datetime_with_fractions_paramter()
        {
            using var context = CreateContext();

            var dateTime = new DateTime(2019, 12, 31, 13, 42, 21, 123);
            var cookies = context.Cookies
                .Where(o => o.PuchasedDateTime == dateTime)
                .ToList();
            
            Assert.Single(cookies);
            Assert.Equal(new DateTime(2019, 12, 31, 13, 42, 21, 123), cookies[0].PuchasedDateTime);
        }

        public class Cookie
        {
            public int CookieId { get; set; }
            public string Name { get; set; }
            public DateTime PuchasedDateTime { get; set; }
            public DateTime BestServedBeforeDateTime { get; set; }
            public DateTime BestServedAfterDateTime { get; set; }
        }

        public class Context : ContextBase
        {
            public DbSet<Cookie> Cookies { get; set; }
            
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Cookie>(
                    entity =>
                    {
                        entity.Property(e => e.BestServedBeforeDateTime)
                            .HasDefaultValue(new DateTime(2021, 12, 31, 13, 42, 21, 123));
                        entity.Property(e => e.BestServedAfterDateTime)
                            .HasDefaultValueSql("'2020-12-31 13:42:21'");

                        entity.HasData(
                            new Cookie
                            {
                                CookieId = 1,
                                Name = "Basic",
                                PuchasedDateTime = new DateTime(2019, 12, 31, 13, 42, 21, 123),
                            },
                            new Cookie
                            {
                                CookieId = 2,
                                Name = "Chocolate Chip",
                                PuchasedDateTime = new DateTime(2019, 12, 24, 18, 09, 04, 234).AddTicks(5678),
                            });
                    });
            }
        }
    }
}
