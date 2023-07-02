# EntityFrameworkCore.Jet
[![Build Status](https://bubibubi.visualstudio.com/EntityFrameworkCore.Jet/_apis/build/status/bubibubi.EntityFrameworkCore.Jet?branchName=master)](https://bubibubi.visualstudio.com/EntityFrameworkCore.Jet/_build/latest?definitionId=1&branchName=master)
[![NuGet](https://img.shields.io/nuget/v/EntityFrameworkCore.Jet.svg?style=flat-square&label=nuget)](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)
[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Jet?style=flat-square)](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)

`EntityFrameworkCore.Jet` is an Entity Framework Core provider for Microsoft Jet/ACE databases (supporting the Microsoft Access database file formats `MDB` and `ACCDB`).

## Compatibility Matrix

| EntityFrameworkCore.Jet Version | EntityFrameworkCore Version | .NET Core |
| ------------- | ------------- | ------------- |
| 7.0.x | 7.0.x | 6.0.x/7.0.x |
| 6.0.x | 6.0.x | 6.0.x |

The major version corresponds to the major version of EF Core (i.e. EFCore.Jet `3.x` is compatible with EF Core `3.y`).
It runs on Windows operating systems only and can be used with either ODBC or OLE DB together with their respective Access Database driver/provider.

## Packages

* [EntityFrameworkCore.Jet](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)
* [EntityFrameworkCore.Jet.Data](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Data/)
* [EntityFrameworkCore.Jet.Odbc](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Odbc/)
* [EntityFrameworkCore.Jet.OleDb](https://www.nuget.org/packages/EntityFrameworkCore.Jet.OleDb/)

## Official Releases
All official releases are available on [nuget.org](https://www.nuget.org/packages/EntityFrameworkCore.Jet/).

## Daily Builds
To use the latest daily builds from our [Azure DevOps feed](https://bubibubi.visualstudio.com/EntityFrameworkCore.Jet/_packaging?_a=feed&feed=public%40Local), add a `NuGet.config` file to your solution root with the following content, and enable _prereleases_:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="efcore.jet" value="https://bubibubi.pkgs.visualstudio.com/EntityFrameworkCore.Jet/_packaging/public/nuget/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```
  
## Further information
More information can be found on our [Wiki](https://www.github.com/bubibubi/EntityFrameworkCore.Jet/wiki).

## Questions
Any questions about how to use `EntityFrameworkCore.Jet` can be ask on [StackOverflow](https://stackoverflow.com/) using the `jet-ef-provider` and `entity-framework-core` tags.
