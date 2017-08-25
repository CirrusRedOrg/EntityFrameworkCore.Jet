using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFramework.SqlServerCompact40.Design.FunctionalTest
{
    public class SqlCeDesignTimeProviderServicesTest : DesignTimeProviderServicesTest
    {
        protected override Assembly GetRuntimeAssembly()
            => typeof(SqlCeDatabaseConnection).GetTypeInfo().Assembly;

        protected override Type GetDesignTimeServicesType()
            => typeof(SqlCeDesignTimeServices);
    }
}