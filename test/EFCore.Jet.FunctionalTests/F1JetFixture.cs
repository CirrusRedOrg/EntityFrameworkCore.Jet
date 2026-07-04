// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

#nullable disable

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class F1JetFixture : F1JetFixtureBase<byte[]>
    {
        protected override void BuildModelExternal(ModelBuilder modelBuilder)
        {
            base.BuildModelExternal(modelBuilder);

            var converter = new BinaryVersionConverter();
            var comparer = new BinaryVersionComparer();

            modelBuilder
                .Entity<Fan>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();

            modelBuilder
                .Entity<FanTpt>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();

            modelBuilder
                .Entity<FanTpc>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();

            modelBuilder
                .Entity<Circuit>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();

            modelBuilder
                .Entity<CircuitTpt>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();

            modelBuilder
                .Entity<CircuitTpc>()
                .Property(e => e.BinaryVersion)
                .HasConversion(converter, comparer)
                .IsRowVersion();
        }

        private class BinaryVersionConverter() : ValueConverter<List<byte>, byte[]>(
            v => v == null ? null : v.ToArray(),
            v => v == null ? null : v.ToList());

        private class BinaryVersionComparer() : ValueComparer<List<byte>>(
            (l, r) => (l == null && r == null) || (l != null && r != null && l.SequenceEqual(r)),
            v => CalculateHashCode(v),
            v => v == null ? null : v.ToList())
        {
            private static int CalculateHashCode(List<byte> source)
            {
                if (source == null)
                {
                    return 0;
                }

                var hash = new HashCode();
                foreach (var el in source)
                {
                    hash.Add(el);
                }

                return hash.ToHashCode();
            }
        }
    }

    public abstract class F1JetFixtureBase<TRowVersion> : F1RelationalFixture<TRowVersion>
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        public override TestHelpers TestHelpers
            => JetTestHelpers.Instance;

        protected override void BuildModelExternal(ModelBuilder modelBuilder)
        {
            base.BuildModelExternal(modelBuilder);

            modelBuilder.Entity<TitleSponsor>()
                .OwnsOne(
                    s => s.Details, eb =>
                    {
                        eb.Property(d => d.Space).HasColumnType("decimal(18,2)");
                    });
        }
    }
}
