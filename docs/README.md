# EntityFrameworkCore.Jet
[![Build Status](https://bubibubi.visualstudio.com/EntityFrameworkCore.Jet/_apis/build/status/bubibubi.EntityFrameworkCore.Jet?branchName=master)](https://bubibubi.visualstudio.com/EntityFrameworkCore.Jet/_build/latest?definitionId=1&branchName=master)
[![NuGet](https://img.shields.io/nuget/v/EntityFrameworkCore.Jet.svg?style=flat-square&label=nuget)](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)

`EntityFrameworkCore.Jet` is an Entity Framework Core provider for Jet (Microsoft Access database file formats `mdb` and `accdb`).

The provider is .NET Standard 2.0 compatible, so it can be used with .NET (Core) 2.0+ and .NET Framework 4.6.1+.

The major version corresponds with the major version of EF Core (i.e. version `3.x` is compatible with EF Core `3.y`).

It runs on Windows operating systems only and can be used with either ODBC or OLE DB together with their corresponding Access Database driver/provider.

## Stable Releases
All stable releases are available on [nuget.org](https://www.nuget.org/packages/EntityFrameworkCore.Jet/).

## Daily Builds
To use the latest daily builds from our Azure DevOps feed, add a `NuGet.config` file to your solution root with the following content and enable _prereleases_:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="EFCore.Jet" value="https://bubibubi.pkgs.visualstudio.com/EntityFrameworkCore.Jet/_packaging/public/nuget/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```
  
## Further information
More information can be found on our [Wiki](https://www.github.com/bubibubi/EntityFrameworkCore.Jet/wiki).

## Questions
Any questions on how to use `EntityFrameworkCore.Jet` can be ask on [StackOverflow](https://stackoverflow.com/) using the `jet-ef-provider` and `entity-framework-core` tags.
