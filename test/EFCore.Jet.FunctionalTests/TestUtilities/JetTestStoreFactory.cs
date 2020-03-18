// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetTestStoreFactory : RelationalTestStoreFactory
    {
        public static JetTestStoreFactory Instance { get; } = new JetTestStoreFactory();

        protected JetTestStoreFactory()
        {
        }

        public override TestStore Create(string storeName)
            => JetTestStore.Create(storeName);

        public override TestStore GetOrCreate(string storeName)
            => JetTestStore.GetOrCreate(storeName);

        public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
            => serviceCollection.AddEntityFrameworkJet();
    }
}
