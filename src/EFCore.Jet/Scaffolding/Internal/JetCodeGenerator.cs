// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    public class JetCodeGenerator : ProviderCodeGenerator
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="JetCodeGenerator" /> class.
        /// </summary>
        /// <param name="dependencies"> The dependencies. </param>
        public JetCodeGenerator([NotNull] ProviderCodeGeneratorDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override MethodCallCodeFragment GenerateUseProvider(
            string connectionString,
            MethodCallCodeFragment providerOptions)
            => new MethodCallCodeFragment(
                nameof(JetDbContextOptionsExtensions.UseJet),
                providerOptions == null
                    ? new object[] { connectionString }
                    : new object[] { connectionString, new NestedClosureCodeFragment("x", providerOptions) });

    }
}
