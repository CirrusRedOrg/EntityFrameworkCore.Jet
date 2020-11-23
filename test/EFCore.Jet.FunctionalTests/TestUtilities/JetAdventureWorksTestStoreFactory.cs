// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetAdventureWorksTestStoreFactory : JetTestStoreFactory
    {
        public static new JetAdventureWorksTestStoreFactory Instance { get; } = new JetAdventureWorksTestStoreFactory();

        protected JetAdventureWorksTestStoreFactory()
        {
        }

        public override TestStore GetOrCreate(string storeName)
            => JetTestStore.GetOrCreate(
                "adventureworks",
                Path.Combine("SqlAzure", "adventureworks.sql"));
    }
}
