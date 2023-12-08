// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetTestHelpers : RelationalTestHelpers
    {
        protected JetTestHelpers()
        {
        }

        public static JetTestHelpers Instance { get; } = new JetTestHelpers();

        public override IServiceCollection AddProviderServices(IServiceCollection services)
            => services.AddEntityFrameworkJet();

        public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseJet(new JetConnection("Database=DummyDatabase"));

        public override LoggingDefinitions LoggingDefinitions { get; } = new JetLoggingDefinitions();

        public string GetStoreName(string storeNameWithoutSuffix) => $"{storeNameWithoutSuffix}.accdb";

        public static DateTimeOffset GetExpectedValue(DateTimeOffset value)
        {
            var val = value.UtcDateTime;
            return new DateTimeOffset(new DateTime(val.Year, val.Month, val.Day, val.Hour, val.Minute, val.Second), TimeSpan.Zero);
        }
    }
}
