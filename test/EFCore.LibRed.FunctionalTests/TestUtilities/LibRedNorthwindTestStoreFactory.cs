// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class LibRedNorthwindTestStoreFactory : LibRedTestStoreFactory
    {
        public const string Name = "Northwind";
        public static readonly string NorthwindConnectionString = LibRedTestStore.CreateConnectionString(Name);
        public new static LibRedNorthwindTestStoreFactory Instance { get; } = new();

        protected LibRedNorthwindTestStoreFactory()
        {
        }

        public override TestStore GetOrCreate(string storeName)
            => LibRedTestStore.GetOrCreateWithScriptPath(Name, scriptPath: "Northwind.sql"/*, templatePath: "Northwind.accdb"*/);
    }
}
