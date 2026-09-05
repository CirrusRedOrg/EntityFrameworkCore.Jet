# EntityFrameworkCore.Jet
[![Build status](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/actions/workflows/push.yml/badge.svg?branch=master)](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/actions/workflows/push.yml)
[![Stable release feed for official builds](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Jet.svg?style=flat-square&label=NuGet)](https://www.nuget.org/packages/EntityFrameworkCore.Jet/)
[![CI build feed for release builds](https://img.shields.io/myget/cirrusred/vpre/EntityFrameworkCore.Jet.svg?label=CI%20Release)](https://www.myget.org/feed/cirrusred/package/nuget/EntityFrameworkCore.Jet)
[![CI build feed for debugging enabled builds](https://img.shields.io/myget/cirrusred-debug/vpre/EntityFrameworkCore.Jet.svg?label=CI%20Debug)](https://www.myget.org/feed/cirrusred-debug/package/nuget/EntityFrameworkCore.Jet)

`EntityFrameworkCore.Jet` is an Entity Framework Core provider for Microsoft Jet/ACE databases (supporting the Microsoft Access database file formats `MDB` and `ACCDB`).

## Compatibility Matrix

| EntityFrameworkCore.Jet Version | Entity Framework Core Version | .NET | Notes |
| ------------- | ------------- | ------------- | ------------- |
| 11.0.x | 11.0.x | 11.0+ | Current development line |
| 10.0.x | 10.0.x | 10.0+ | Supported |
| 9.0.x | 9.0.x | 9.0+ | Supported |
| 8.0.x | 8.0.x | 8.0+ | Alpha 2 onwards is compatible with EF Core RTM |
| 7.0.x | 7.0.x | 6.0+ |
| 6.0.x | 6.0.x | 6.0+ |

The major version corresponds to the major version of EF Core (i.e. EFCore.Jet `3.x` is compatible with EF Core `3.y`).
It runs on Windows operating systems only and can be used with either ODBC or OLE DB together with their respective Access Database driver/provider.

## Requirements

`EntityFrameworkCore.Jet` requires:

* Windows.
* A Microsoft Access Database Engine driver/provider for either ODBC or OLE DB.
* A process architecture (`x86` or `x64`) that matches the installed Access driver/provider.

The provider works with Microsoft Access `MDB` and `ACCDB` database files.

## LibRed - native managed engine (in development)

[LibRed](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/blob/master/src/LibRed/README.md) is a
from-scratch, fully managed implementation of the Jet/ACE engine that also lives in this repository. It reads
and writes `MDB`/`ACCDB` files **directly** - no ODBC, OLE DB, DAO or ADOX - so **none of the requirements
above apply to it**: no Windows, no installed Access driver, and no need to match your process architecture to
one. It runs on Linux, macOS and ARM64, which CI proves by running its suites on all five platforms with no
Access engine installed anywhere.

It reads and writes real database files, creates them from nothing (no DAO, no template file), runs SQL end to
end, and an EF Core `DbContext` round-trips through it. The on-disk format it depends on is documented and
verified in
[`src/LibRed/docs/format/`](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/blob/master/src/LibRed/docs/format/README.md).

```csharp
optionsBuilder.UseLibRed(@"C:\Data\Blogging.accdb");
```

Because LibRed owns both the SQL generator and the engine that parses the result, it does not have to please
ACE. `UseLibRed` takes an optional `LibRedSqlMode`:

* `LibRedSqlMode.Extended` (the default) - LibRed's own SQL generator, free of the Jet dialect's limitations.
  It emits standard SQL that ACE has no syntax for at all: `CROSS`/`OUTER APPLY`, window functions,
  `OFFSET`/`FETCH` paging, `FULL OUTER JOIN`, `CASE`, `COALESCE`, `NULLIF`, set operations inside subquery
  predicates, and flat unparenthesised join chains.
* `LibRedSqlMode.Compatible` - the same SQL generator the `EntityFrameworkCore.Jet` provider uses, so the
  statements LibRed receives are ones ACE would also accept. Use it when the same queries have to run against
  both engines.

Each mode has its own EF Core specification suite, since the two produce different SQL for the same query.

## Packages

The Jet provider (Windows, ACE driver required):

* [EntityFrameworkCore.Jet](https://www.nuget.org/packages/EntityFrameworkCore.Jet/) - the EF Core provider.
* [EntityFrameworkCore.Jet.Data](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Data/) - the shared ADO.NET-style data access layer used by the provider packages.
* [EntityFrameworkCore.Jet.Odbc](https://www.nuget.org/packages/EntityFrameworkCore.Jet.Odbc/) - ODBC support, including the `UseJetOdbc` extension method.
* [EntityFrameworkCore.Jet.OleDb](https://www.nuget.org/packages/EntityFrameworkCore.Jet.OleDb/) - OLE DB support, including the `UseJetOleDb` extension method.

Shared by both providers:

* `EntityFrameworkCore.Jet.Common` - the Jet-dialect EF Core services (query pipeline and translators, migrations SQL generation, conventions, annotations, value generation). Cross-platform, and it does not reference the ACE-bound data layer, which is what lets LibRed sit on it.

LibRed, the native managed engine (cross-platform, no driver):

* `LibRed.Core` - the `MDB`/`ACCDB` file format reader/writer.
* `LibRed.Sql` - the SQL front end (ANTLR grammar, AST, binder).
* `LibRed.Engine` - the query planner and executor.
* `LibRed.Ado` - the ADO.NET surface.
* `EntityFrameworkCore.LibRed` - the EF Core provider, including `UseLibRed`.

## Getting Started

Install the provider package for the data access technology you want to use:

```powershell
dotnet add package EntityFrameworkCore.Jet.OleDb
```

or:

```powershell
dotnet add package EntityFrameworkCore.Jet.Odbc
```

Configure your `DbContext` with the matching `UseJet...` extension method:

```csharp
using Microsoft.EntityFrameworkCore;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseJetOleDb(
            @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Data\Blogging.accdb");
}

public class Blog
{
    public int Id { get; set; }
    public string? Url { get; set; }
}
```

For ODBC, use `UseJetOdbc` instead:

```csharp
optionsBuilder.UseJetOdbc(
    @"Driver={Microsoft Access Driver (*.mdb, *.accdb)};Dbq=C:\Data\Blogging.accdb;");
```

## NuGet Feeds

### Official Releases

All official releases are available on [nuget.org](https://www.nuget.org/packages/EntityFrameworkCore.Jet/).

### CI Builds

CI publishes each build to MyGet. To use the latest CI builds, add a `NuGet.config` file to your solution root, add the feeds you are interested in and enable _prereleases_:

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

There are two CI build feeds available, one with (optimized) `Release` configuration builds and one with (unoptimized) `Debug` configuration builds.
All packages use SourceLink.
  
## Fluent API

In order to simplify writing code for more than just one provider, some Fluent API method names have been made specific to Jet.
Examples are:

* `UseIdentityColumn` -> `UseJetIdentityColumn`
* `UseIdentityColumns` -> `UseJetIdentityColumns`

## Further information

More information can be found on our [Wiki](https://www.github.com/CirrusRedOrg/EntityFrameworkCore.Jet/wiki).

## Questions

Questions, bug reports, and feature requests can be opened as [GitHub issues](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/issues).
