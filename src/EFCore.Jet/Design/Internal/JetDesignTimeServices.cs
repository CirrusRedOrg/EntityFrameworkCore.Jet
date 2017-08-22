// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Scaffolding.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Design.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDesignTimeServices : IDesignTimeServices
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
            => serviceCollection
                .AddSingleton<IRelationalTypeMapper, JetTypeMapper>()
                .AddSingleton<IDatabaseModelFactory, JetDatabaseModelFactory>()
                .AddSingleton<IScaffoldingProviderCodeGenerator, JetScaffoldingCodeGenerator>()
                .AddSingleton<IAnnotationCodeGenerator, JetAnnotationCodeGenerator>();
    }
}
