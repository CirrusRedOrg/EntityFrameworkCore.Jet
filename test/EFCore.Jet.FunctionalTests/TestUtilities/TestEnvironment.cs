// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.IO;
using EntityFrameworkCore.Jet.Data;
using Microsoft.Extensions.Configuration;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class TestEnvironment
    {
        public static IConfiguration Config { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json", optional: true)
            .AddJsonFile("config.test.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetSection("Test:Jet");

        public static string DefaultConnection { get; } = Environment.GetEnvironmentVariable("EFCoreJet_DefaultConnection") ??
                                                          Config["DefaultConnection"] ??
                                                          JetConnection.GetConnectionString("Jet.accdb", default(DataAccessProviderType));

        public static bool IsConfigured
        {
            get
            {
                var dataAccessProviderType = JetConnection.GetDataAccessProviderType(DefaultConnection);
                var dataAccessProviderFactory = JetFactory.GetDataAccessProviderFactory(dataAccessProviderType);
                var connectionStringBuilder = dataAccessProviderFactory.CreateConnectionStringBuilder()!;
                connectionStringBuilder.ConnectionString = DefaultConnection;
                
                return !string.IsNullOrEmpty(connectionStringBuilder.GetDataSource());
            }
        }

        public static DataAccessProviderType DataAccessProviderType { get; } = JetConnection.GetDataAccessProviderType(DefaultConnection);
        public static DbProviderFactory DataAccessProviderFactory { get; } = JetFactory.GetDataAccessProviderFactory(JetConnection.GetDataAccessProviderType(DefaultConnection));
        
        public static bool IsCI { get; } = Environment.GetEnvironmentVariable("PIPELINE_WORKSPACE") != null
            || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null;

        public static bool? GetFlag(string key)
            => bool.TryParse(Config[key], out var flag) ? flag : null;

        public static int? GetInt(string key)
            => int.TryParse(Config[key], out var value) ? value : null;
    }
}
