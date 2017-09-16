using System;
using System.Reflection;
using EntityFrameworkCore.Jet.Design.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;

namespace EntityFrameworkCore.Jet.Tests.Design
{
    public class JetDesignTimeProviderServicesTest : DesignTimeProviderServicesTest
    {
        protected override Assembly GetRuntimeAssembly()
            => typeof(JetRelationalConnection).GetTypeInfo().Assembly;

        protected override Type GetDesignTimeServicesType()
            => typeof(JetDesignTimeServices);
    }
}