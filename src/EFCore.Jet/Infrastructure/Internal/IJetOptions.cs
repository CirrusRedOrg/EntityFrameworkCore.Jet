// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;

namespace EntityFrameworkCore.Jet.Infrastructure.Internal
{
    /// <summary>
    ///     Options set at the <see cref="IServiceProvider" /> singleton level to control Jet specific options.
    /// </summary>
    public interface IJetOptions : ISingletonOptions
    {
        string? ConnectionString { get; }
        DataAccessProviderType DataAccessProviderType { get; }
        bool UseOuterSelectSkipEmulationViaDataReader { get; }
        bool EnableMillisecondsSupport { get; }
        bool UseShortTextForSystemString { get; }
    }
}
