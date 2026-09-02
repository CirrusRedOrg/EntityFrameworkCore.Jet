// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Infrastructure;

namespace EntityFrameworkCore.LibRed.Scaffolding.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    /// <remarks>
    ///     Initializes a new instance of the <see cref="LibRedCodeGenerator" /> class.
    /// </remarks>
    /// <param name="dependencies"> The dependencies. </param>
    public class LibRedCodeGenerator(ProviderCodeGeneratorDependencies dependencies) : ProviderCodeGenerator(dependencies)
    {
        private static readonly MethodInfo _useLibRedMethodInfo
            = typeof(LibRedDbContextOptionsBuilderExtensions).GetRuntimeMethod(
                nameof(LibRedDbContextOptionsBuilderExtensions.UseLibRed),
                [typeof(DbContextOptionsBuilder), typeof(string), typeof(Action<LibRedDbContextOptionsBuilder>)])!;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override MethodCallCodeFragment GenerateUseProvider(
            string connectionString,
            MethodCallCodeFragment? providerOptions)
            => new(_useLibRedMethodInfo,
                providerOptions == null
                    ? [connectionString]
                    : [connectionString, new NestedClosureCodeFragment("x", providerOptions)]);
    }
}
