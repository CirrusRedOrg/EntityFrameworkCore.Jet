// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFramework.Jet.FunctionalTests.TestUtilities
{
    public class JetNorthwindTestStoreFactory : JetTestStoreFactory
    {
        public const string Name = "NorthwindEF7";
        public static readonly string NorthwindConnectionString = JetTestStore.CreateConnectionString(Name);
        public new static JetNorthwindTestStoreFactory Instance { get; } = new JetNorthwindTestStoreFactory();

        protected JetNorthwindTestStoreFactory()
        {
        }

        public override TestStore GetOrCreate(string storeName)
            => JetTestStore.GetOrCreate(Name, "Northwind.sql");
    }
}
