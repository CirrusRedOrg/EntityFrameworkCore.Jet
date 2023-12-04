# EntityFrameworkCore.Jet
[![Build status](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/actions/workflows/build.yml)
[![Stable release feed for official builds](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Jet.svg?style=flat-square&label=NuGet)](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)
[![Nightly build feed for release builds](https://img.shields.io/myget/cirrusred/vpre/EntityFrameworkCore.Jet.svg?label=Nightly)](https://www.myget.org/feed/cirrusred/package/nuget/EntityFrameworkCore.Jet)
[![Nightly build feed for debugging enabled builds](https://img.shields.io/myget/cirrusred-debug/vpre/EntityFrameworkCore.Jet.svg?label=Debug)](https://www.myget.org/feed/cirrusred-debug/package/nuget/EntityFrameworkCore.Jet)

`EntityFrameworkCore.Jet` is an Entity Framework Core provider for Microsoft Jet/ACE databases (supporting the Microsoft Access database file formats `MDB` and `ACCDB`).

## Compatibility Matrix

| EntityFrameworkCore.Jet Version | EntityFrameworkCore Version | .NET Core | Notes |
| ------------- | ------------- | ------------- | ------------- |
| 8.0.x | 8.0.x | 8.0.x | Alpha 2 onwards is compatible with EF Core RTM |
| 7.0.x | 7.0.x | 6.0.x/7.0.x |
| 6.0.x | 6.0.x | 6.0.x |

The major version corresponds to the major version of EF Core (i.e. EFCore.Jet `3.x` is compatible with EF Core `3.y`).
It runs on Windows operating systems only and can be used with either ODBC or OLE DB together with their respective Access Database driver/provider.

## Packages

* [EntityFrameworkCore.Jet](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)
* [EntityFrameworkCore.Jet.Data](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Data/)
* [EntityFrameworkCore.Jet.Odbc](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Odbc/)
* [EntityFrameworkCore.Jet.OleDb](https://www.nuget.org/packages/EntityFrameworkCore.Jet.OleDb/)

## NuGet Feeds

### Official Releases

All official releases are available on [nuget.org](https://www.nuget.org/packages/EntityFrameworkCore.Jet/).

### Daily Builds

To use the latest daily builds, add a `NuGet.config` file to your solution root, add the daily feeds you are interested in and enable _prereleases_:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="efcorejet-daily" value="https://www.myget.org/F/cirrusred/api/v3/index.json" />
    <add key="efcorejet-daily-debug" value="https://www.myget.org/F/cirrusred-debug/api/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

There are two daily build feeds available, one with (optimized) `Release` configuration builds and one with (unoptimized) `Debug` configuration builds.
All packages use SourceLink.
  
## Fluent API

In order to simplify writing code for more than just one provider, some Fluent API method names have been made specific to Jet.
Examples are:

* `UseIdentityColumn` -> `UseJetIdentityColumn`
* `UseIdentityColumns` -> `UseJetIdentityColumns`

## Further information

More information can be found on our [Wiki](https://www.github.com/CirrusRedOrg/EntityFrameworkCore.Jet/wiki).

## Questions

Any questions about how to use `EntityFrameworkCore.Jet` can be ask on [StackOverflow](https://stackoverflow.com/) using the `jet-ef-provider` and `entity-framework-core` tags.
