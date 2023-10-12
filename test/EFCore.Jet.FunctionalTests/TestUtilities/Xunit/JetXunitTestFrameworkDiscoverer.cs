using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer
{
    public JetXunitTestFrameworkDiscoverer(
        IAssemblyInfo assemblyInfo,
        ISourceInformationProvider sourceProvider,
        IMessageSink diagnosticMessageSink,
        IXunitTestCollectionFactory collectionFactory = null)
        : base(
            assemblyInfo,
            sourceProvider,
            diagnosticMessageSink,
            collectionFactory)
    {
        // Prime the cache with our own discoverers, so they get used over the original ones from EF Core.
        DiscovererTypeCache.Add(typeof(ConditionalFactAttribute), typeof(JetConditionalFactDiscoverer));
        DiscovererTypeCache.Add(typeof(ConditionalTheoryAttribute), typeof(JetConditionalTheoryDiscoverer));
    }

    protected override bool IsValidTestClass(ITypeInfo type)
        => base.IsValidTestClass(type) /* &&
           IsTestConditionMet<SupportedServerVersionConditionAttribute>(type) &&
           IsTestConditionMet<SupportedServerVersionLessThanConditionAttribute>(type)*/;

    protected virtual bool IsTestConditionMet<TType>(ITypeInfo type) where TType : ITestCondition
        => GetTestConditions<TType>(type).Aggregate(true, (current, next) => current && next.IsMetAsync().Result);

    protected virtual IEnumerable<ITestCondition> GetTestConditions<TType>(ITypeInfo type) where TType : ITestCondition
        => type.GetCustomAttributes(typeof(TType))
            .Select(attribute => (TType)Activator.CreateInstance(typeof(TType), attribute.GetConstructorArguments().ToArray()))
            .Cast<ITestCondition>();
}