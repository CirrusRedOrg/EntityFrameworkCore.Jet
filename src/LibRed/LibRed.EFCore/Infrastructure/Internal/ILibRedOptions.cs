// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EntityFrameworkCore.LibRed.Infrastructure.Internal
{
    /// <summary>
    ///     Options set at the <see cref="IServiceProvider" /> singleton level to control LibRed specific options.
    /// </summary>
    public interface ILibRedOptions : ISingletonOptions
    {
        string? ConnectionString { get; }
        bool UseShortTextForSystemString { get; }
        LibRedSqlMode SqlMode { get; }
    }
}
