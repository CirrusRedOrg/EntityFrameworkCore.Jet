// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class LibRedAdventureWorksTestStoreFactory : LibRedTestStoreFactory
    {
        public new static LibRedAdventureWorksTestStoreFactory Instance { get; } = new();

        protected LibRedAdventureWorksTestStoreFactory()
        {
        }

        public override TestStore GetOrCreate(string storeName)
            => LibRedTestStore.GetOrCreateWithScriptPath(
                "adventureworks",
                Path.Combine("SqlAzure", "adventureworks.sql"));
    }
}
