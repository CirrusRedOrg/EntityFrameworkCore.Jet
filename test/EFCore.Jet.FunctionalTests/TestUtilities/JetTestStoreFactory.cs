// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests.TestUtilities
{
    public class JetTestStoreFactory : ITestStoreFactory
    {
        public static JetTestStoreFactory Instance { get; } = new JetTestStoreFactory();

        protected JetTestStoreFactory()
        {
        }

        public virtual TestStore Create(string storeName)
            => JetTestStore.Create(storeName);

        public virtual TestStore GetOrCreate(string storeName)
            => JetTestStore.GetOrCreate(storeName);

        public virtual IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
            => serviceCollection.AddEntityFrameworkJet()
                .AddSingleton<ILoggerFactory>(new TestSqlLoggerFactory());
    }
}
