// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using System;
using System.Data.Common;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public static class TestEnvironment
    {
        public static IConfiguration Config { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json", optional: true)
            .AddJsonFile("config.test.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetSection("Test:LibRed");

        public static string DefaultConnection { get; } = Environment.GetEnvironmentVariable("EFCoreLibRed_DefaultConnection") ??
                                                          Config["DefaultConnection"] ??
                                                          LibRedConnection.GetConnectionString("LibRed.accdb");

        public static bool IsConfigured
        {
            get
            {
                var dataAccessProviderFactory = LibRedFactory.GetDataAccessProviderFactory();
                var connectionStringBuilder = (LibRedConnectionStringBuilder)dataAccessProviderFactory.CreateConnectionStringBuilder()!;
                connectionStringBuilder.ConnectionString = DefaultConnection;

                return !string.IsNullOrEmpty(connectionStringBuilder.DataSource);
            }
        }

        public static DbProviderFactory DataAccessProviderFactory { get; } = LibRedFactory.GetDataAccessProviderFactory();
        
        public static bool IsCI { get; } = Environment.GetEnvironmentVariable("PIPELINE_WORKSPACE") != null
            || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null;

        public static bool? GetFlag(string key)
            => bool.TryParse(Config[key], out var flag) ? flag : null;

        public static int? GetInt(string key)
            => int.TryParse(Config[key], out var value) ? value : null;
    }
}
