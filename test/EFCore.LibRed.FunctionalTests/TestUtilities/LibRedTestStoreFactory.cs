// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class LibRedTestStoreFactory : RelationalTestStoreFactory
    {
        public static LibRedTestStoreFactory Instance { get; } = new();

        protected LibRedTestStoreFactory()
        {
        }

        public override TestStore Create(string storeName)
            => LibRedTestStore.Create(storeName);

        public override TestStore GetOrCreate(string storeName)
            => LibRedTestStore.GetOrCreate(storeName);

        public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
            => serviceCollection.AddEntityFrameworkLibRed();
    }
}
