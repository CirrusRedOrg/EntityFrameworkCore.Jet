// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
#nullable disable
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class SequentialGuidEndToEndTest : IAsyncLifetime
    {
        [ConditionalFact]
        public async Task Can_use_sequential_GUID_end_to_end_async()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .BuildServiceProvider();

            using (var context = new BronieContext(serviceProvider, TestStore.Name))
            {
                context.Database.EnsureCreatedResiliently();

                for (var i = 0; i < 50; i++)
                {
                    await context.AddAsync(
                        new Pegasus { Name = "Rainbow Dash " + i });
                }

                await context.SaveChangesAsync();
            }

            using (var context = new BronieContext(serviceProvider, TestStore.Name))
            {
                var pegasuses = await context.Pegasuses.OrderBy(e => e.Id).ToListAsync();

                for (var i = 0; i < 50; i++)
                {
                    Assert.Equal("Rainbow Dash " + i, pegasuses[i].Name);
                }
            }
        }

        [ConditionalFact]
        public async Task Can_use_explicit_values()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .BuildServiceProvider();

            var guids = new List<Guid>();

            using (var context = new BronieContext(serviceProvider, TestStore.Name))
            {
                context.Database.EnsureCreatedResiliently();

                for (var i = 0; i < 50; i++)
                {
                    guids.Add(
                        context.Add(
                            new Pegasus
                            {
                                Name = "Rainbow Dash " + i,
                                Index = i,
                                Id = Guid.NewGuid()
                            }).Entity.Id);
                }

                await context.SaveChangesAsync();
            }

            using (var context = new BronieContext(serviceProvider, TestStore.Name))
            {
                var pegasuses = await context.Pegasuses.OrderBy(e => e.Index).ToListAsync();

                for (var i = 0; i < 50; i++)
                {
                    Assert.Equal("Rainbow Dash " + i, pegasuses[i].Name);
                    Assert.Equal(guids[i], pegasuses[i].Id);
                }
            }
        }

        private class BronieContext(IServiceProvider serviceProvider, string databaseName) : DbContext
        {
            public DbSet<Pegasus> Pegasuses { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .UseJet(JetTestStore.CreateConnectionString(databaseName),
                        TestEnvironment.DataAccessProviderFactory, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(serviceProvider);
        }

        private class Pegasus
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public int Index { get; set; }
        }

        protected JetTestStore TestStore { get; private set; }

        public async Task InitializeAsync()
            => TestStore = await JetTestStore.CreateInitializedAsync("SequentialGuidEndToEndTest");

        public Task DisposeAsync()
        {
            TestStore.Dispose();
            return Task.CompletedTask;
        }

        [ConditionalFact]
        public void CustomUuid7Test()
        {
            DateTimeOffset dtoNow = DateTimeOffset.UtcNow;
            Guid net9internal = Guid.CreateVersion7(dtoNow);
            Guid custom = Next(dtoNow);
            var bytenet9 = net9internal.ToByteArray().AsSpan(0, 6);
            var bytecustom = custom.ToByteArray().AsSpan(0,6);
            Assert.Equal(bytenet9,bytecustom);
            Assert.Equal(net9internal.Version,custom.Version);
            var t1 = net9internal.Variant & Variant10xxMask;
            var t2 = BitConverter.GetBytes(custom.Variant);
            Assert.InRange(net9internal.Variant,8,0xB);
            Assert.InRange(custom.Variant, 8, 0xB);
        }

        private const byte Variant10xxValue = 0x80;
        private const ushort Version7Value = 0x7000;
        private const ushort VersionMask = 0xF000;
        private const byte Variant10xxMask = 0xC0;

        private Guid Next(DateTimeOffset timeStamp)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            var succeeded = Guid.NewGuid().TryWriteBytes(guidBytes);
            var unixms = timeStamp.ToUnixTimeMilliseconds();
            Span<byte> counterBytes = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(counterBytes, in unixms);

            if (!BitConverter.IsLittleEndian)
            {
                counterBytes.Reverse();
            }

            //unix ts ms - 48 bits (6 bytes)
            guidBytes[00] = counterBytes[2];
            guidBytes[01] = counterBytes[3];
            guidBytes[02] = counterBytes[4];
            guidBytes[03] = counterBytes[5];
            guidBytes[04] = counterBytes[0];
            guidBytes[05] = counterBytes[1];

            //UIDv7 version - first 4 bits (1/2 byte) of the next 16 bits (2 bytes)
            var _c = BitConverter.ToInt16(guidBytes.Slice(6, 2));
            _c = (short)((_c & ~VersionMask) | Version7Value);
            BitConverter.TryWriteBytes(guidBytes.Slice(6, 2), _c);

            //2 bit variant
            //first 2 bits of the next 64 bits (8 bytes)
            guidBytes[8] = (byte)((guidBytes[8] & ~Variant10xxMask) | Variant10xxValue);
            return new Guid(guidBytes);
        }
    }
}
