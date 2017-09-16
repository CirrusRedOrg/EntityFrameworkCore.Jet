// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using EntityFrameworkCore.Jet.Storage.Internal;

namespace EntityFrameworkCore.Jet.Tests
{
    public class ApiConsistencyTest : ApiConsistencyTestBase
    {
        protected override Assembly TargetAssembly => typeof(JetRelationalConnection).GetTypeInfo().Assembly;
    }
}
