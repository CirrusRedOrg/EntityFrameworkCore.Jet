// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetExecutionStrategyFactory : RelationalExecutionStrategyFactory
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetExecutionStrategyFactory(
            [NotNull] ExecutionStrategyDependencies dependencies)
            : base(dependencies)
        {
        }

        protected override IExecutionStrategy CreateDefaultStrategy(ExecutionStrategyDependencies dependencies)
            => new JetExecutionStrategy(dependencies);
    }
}
