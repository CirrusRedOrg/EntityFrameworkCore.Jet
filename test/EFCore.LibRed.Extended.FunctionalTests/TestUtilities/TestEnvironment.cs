// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities
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
                var connectionStringBuilder = new LibRedConnectionStringBuilder { ConnectionString = DefaultConnection };

                return !string.IsNullOrEmpty(connectionStringBuilder.DataSource);
            }
        }

        public static bool IsCI { get; } = Environment.GetEnvironmentVariable("PIPELINE_WORKSPACE") != null
            || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null;

        /// <summary>The positive form of <see cref="IsCI"/>, for ConditionalClass, which runs a
        /// class when its named condition member is true.</summary>
        public static bool IsNotCI => !IsCI;

        public static bool? GetFlag(string key)
            => bool.TryParse(Config[key], out var flag) ? flag : null;

        public static int? GetInt(string key)
            => int.TryParse(Config[key], out var value) ? value : null;
    }
}
