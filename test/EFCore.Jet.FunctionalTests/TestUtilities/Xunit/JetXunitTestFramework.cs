using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestFramework : XunitTestFramework
{
    public JetXunitTestFramework(IMessageSink messageSink) : base(messageSink)
    {
    }

    protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
        => new JetXunitTestFrameworkDiscoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);
}