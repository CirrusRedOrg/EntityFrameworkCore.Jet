using System;
using System.IO;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;

public class JetXunitTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    public const string TestsKnownToCrashTestRunnerFilePath = "./../../../TestsKnownToCrashTestRunner.txt";
    public const string DetectCrashesOfPreviousRunsEnvironmentVariableName = "EFCoreJet_DetectCrashesOfPreviousRuns";

    public virtual bool EnableDetectCrashesOfPreviousRuns
        => Environment.GetEnvironmentVariable(DetectCrashesOfPreviousRunsEnvironmentVariableName)?.ToLowerInvariant() == "true";

    protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
    {
        if (EnableDetectCrashesOfPreviousRuns)
        {
            DetectCrashesOfPreviousRuns();
        }
        
        return new JetXunitTestFrameworkDiscoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);
    }

    protected virtual void DetectCrashesOfPreviousRuns()
    {
        if (!Directory.Exists(JetXunitTestCaseRunner.TestRunnerCrashCacheDirectory))
            return;
        
        foreach (var filePath in Directory.EnumerateFiles(JetXunitTestCaseRunner.TestRunnerCrashCacheDirectory, "*.txt"))
        {
            var contents = File.ReadAllText(filePath);
            contents = $"{string.Join('\t', Path.GetFileNameWithoutExtension(filePath).Split('_'))}\t{contents}\n";
            File.AppendAllText(TestsKnownToCrashTestRunnerFilePath, contents);
            
            File.Delete(filePath);
        }
    }
}