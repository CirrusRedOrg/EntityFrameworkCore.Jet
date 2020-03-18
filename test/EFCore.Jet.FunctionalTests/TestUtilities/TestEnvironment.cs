// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Data.Jet;
using System.Data.OleDb;
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

        public static string DefaultConnection { get; } = Config["DefaultConnection"]
            ?? JetConnection.GetConnectionString("Jet.accdb");

        private static readonly string _dataSource = new OleDbConnectionStringBuilder(DefaultConnection).DataSource;

        public static bool IsConfigured { get; } = !string.IsNullOrEmpty(_dataSource);

        public static bool IsLocalDb { get; } = true;

        public static bool IsCI { get; } = Environment.GetEnvironmentVariable("PIPELINE_WORKSPACE") != null
            || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null;

        public static bool? GetFlag(string key)
            => bool.TryParse(Config[key], out var flag) ? flag : (bool?)null;

        public static int? GetInt(string key)
            => int.TryParse(Config[key], out var value) ? value : (int?)null;
    }
}
