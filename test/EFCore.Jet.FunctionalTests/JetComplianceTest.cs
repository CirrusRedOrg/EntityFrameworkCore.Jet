// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class JetComplianceTest : RelationalComplianceTestBase
    {
        protected override Assembly TargetAssembly { get; } = typeof(JetComplianceTest).Assembly;
    }
}
