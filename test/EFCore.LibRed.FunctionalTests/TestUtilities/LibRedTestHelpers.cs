// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class LibRedTestHelpers : RelationalTestHelpers
    {
        protected LibRedTestHelpers()
        {
        }

        public static LibRedTestHelpers Instance { get; } = new();

        public override IServiceCollection AddProviderServices(IServiceCollection services)
            => services.AddEntityFrameworkLibRed();

        public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseLibRed(new LibRedConnection("Database=DummyDatabase"));

        public override LoggingDefinitions LoggingDefinitions { get; } = new JetLoggingDefinitions();

        public static DateTimeOffset GetExpectedValue(DateTimeOffset value)
        {
            var val = value.UtcDateTime;
            return new DateTimeOffset(new DateTime(val.Year, val.Month, val.Day, val.Hour, val.Minute, val.Second, val.Millisecond), TimeSpan.Zero);
        }
    }
}
