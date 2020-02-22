# Notes for Developers/Builders

## Important Notes as of Feb 21, 2020
- System.Data.OleDb currently has several issues with x86 on .NET Core (see below).  If needing to test on .NET Core, using the 64-bit Access OleDb driver instead is recommended.  Otherwise, tests can still be run against .NET Framework (x86 or x64).

## System.Data.OleDb Issues
- Current Nuget.org package (version 4.7.0) contains several significant bugs:
    - https://github.com/dotnet/runtime/issues/32509
    - (fixed, awaiting merge) https://github.com/dotnet/runtime/issues/31177
    - (fixed, merged) https://github.com/dotnet/runtime/issues/981
- Use NuGet.Config (see below) to get nightly builds instead of using the published version

## Nuget.Config
- Used to get latest nightly builds of System.Data.OleDb
- When a public release with bug fixes is posted to NuGet.org, having the NuGet.Config file in the EFCore.Jet solution likely won't be necessary

## .NET Core 3.0/3.1 Issues
- Using the dynamic keyword with COM objects not supported on .NET Core 3.0/3.1
- Instead, System.Data.Jet.csproj includes two COMReference nodes for ADODB.dll & ADOX.dll, which generate strongly-typed references to these libraries.  No x86/x64 issues have been encountered during preliminary tests, but I believe .NET 5.0 may support using the dynamic keyword with COM objects, so the code could possibly be reverted to the way it was, if issues with the COM wrappers are found.
- Affects AdoxWrapper.cs, JetStoreSchemaDefinitionRetrieve.cs & JetSyntaxHelper.cs
- FYI: Paths to msadox.dll:
    - C:\Program Files\Common Files\System\ado
    - C:\Program Files (x86)\Common Files\System\ado

## Directory.Build.targets
- Used due to conflict between the EF Core 2.2 and .NET Core 3.1 versions of IAsyncGrouping<,> & IAsyncEnumerable<>.  Invoked via setting an Alias attribute on the appropriate PackageReference.
- Affects SharedTypeExtensions.cs
- Likely is not be needed after upgrading to use EF Core 3.1 on .NET Core 3.1

## Test Design Guidelines
- Consider extracting out the following settings to a single, global test configuration file:
    - Provider [string]: "Microsoft.ACE.OLEDB.15.0"
    - TempDirectoryPath [string]: "C:\TEMP"
    - RunSqlCeTests [bool] : true/false
    - RunSqlServerTests [bool] : true/false
    - RunSqliteTests [bool] : true/false

## Prerequisites for Building/Running Tests
- Install SQL Compact 4.0
  - x86 or x64 (whichever applies to the version of Windows used)
- Install Microsoft Access 2013 Runtime (https://www.microsoft.com/en-us/download/details.aspx?id=39358)
  - x86, x64, or Both (not sure if installing both x86 and x64 side-by-side works properly)
- The folder "C:\TEMP" must exist, certain tests will throw if it does not

## Test Runner Issues
- Test projects need to use a NuGet package reference of Microsoft.NET.Test.SDK version 16.4.0 or higher to switch between x86/x64, otherwise Visual Studio test runner does not respect the "Processor Architecture for Any CPU" setting and still tries to run x86 tests on x64.
    - Refer to: https://developercommunity.visualstudio.com/content/problem/697732/test-runner-wont-execute-net-core-tests-in-32-bit.html
- Ensure xUnit test projects contain a reference to the nuget package xunit.runner.visualstudio to run the test from Visual Studio Test Explorer

## .NET Standard Compatibility
- Should the projects also target .NET Standard 2.0?  Even if the project compiles for .NET Standard 2.0, it will not work across all .NET Standard 2.0 platforms.  In particular, it will only work on Windows, which many users may expect, however, I believe either the COM or System.Data.OleDb (or both) will fail if running on .NET Core 2.0/2.1/2.2, which would be an incompatibility that is more difficult for users to guess.
- Consider targeting both .NET Standard 2.1 and .NET Framework. However, what is the advantage?  Perhaps just Mono on Windows?

## General Resources
- [How to multitarget](https://docs.microsoft.com/en-us/dotnet/core/tutorials/libraries#how-to-multitarget)
- [Microsoft Access SQL reference](https://docs.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/microsoft-access-sql-reference)

## Outdated Notes that Probably are No Longer Important
- Probably want to set Visual Studio to use PackageReference instead of packages.config by default (Options->Nuget Package Manager)