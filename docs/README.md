# EntityFrameworkCore.Jet
This project is the Entity Framework Core provider for Jet (Microsoft Access mdb and accdb file format).

This provider is mainly intended for desktop applications under .NET 4.6.1 (Console, WPF, WinForms and Services), but 
may also work under ASP.NET 4.6 and ASP.NET Core 1.0 when using the .NET Framework runtime. It does **not** work under 
the .NET Core runtime (netcore and netstandard).

You can find the latest build on [NuGet](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)

## Prerequisites for building tests
- Install SQL Compact 4.0
  - x86 or x64 (whichever applies)
- Install Microsoft Access 2013 Runtime (https://www.microsoft.com/en-us/download/details.aspx?id=39358)
  - x86, x64, or Both
- (maybe?) Install Microsoft Access Database Engine 2010 Redistributable (https://www.microsoft.com/en-US/download/details.aspx?id=13255)
  - x86, x64, or Both
- The folder "C:\TEMP" must exist
- Ensure xUnit test projects contain a reference to the nuget package xunit.runner.visualstudio to run the test from Visual Studio Test Explorer
  
## More documentation
More documentation can be found on project [Wiki](https://www.github.com/bubibubi/EntityFrameworkCore.Jet/wiki)

## Questions 
For question on how to use it please use stackoverflow, tags _access-ef-provider_ and _jet-ef-provider_.

