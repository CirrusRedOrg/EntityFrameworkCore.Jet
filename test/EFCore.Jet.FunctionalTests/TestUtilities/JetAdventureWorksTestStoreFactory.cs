// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class JetAdventureWorksTestStoreFactory : JetTestStoreFactory
    {
        public new static JetAdventureWorksTestStoreFactory Instance { get; } = new();

        protected JetAdventureWorksTestStoreFactory()
        {
        }

        public override TestStore GetOrCreate(string storeName)
            => JetTestStore.GetOrCreateWithScriptPath(
                "adventureworks",
                Path.Combine("SqlAzure", "adventureworks.sql"));
    }
}
