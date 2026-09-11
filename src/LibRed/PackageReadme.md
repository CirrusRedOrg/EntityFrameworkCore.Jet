# LibRed

A from-scratch, fully managed implementation of the Microsoft Jet/ACE database engine — the format behind
Access `.mdb` and `.accdb` files.

LibRed reads, writes and creates those files **directly**. There is no ODBC, no OLE DB, no DAO and no ADOX,
so there is no Access Database Engine to install, no Windows requirement, and no need to match your process
architecture to a driver. It runs on Linux, macOS and ARM64, and its test suites run on all of them with no
Access engine present anywhere.

It ships with an Entity Framework Core provider.

## Getting started

```
dotnet add package EntityFrameworkCore.LibRed
```

```csharp
using Microsoft.EntityFrameworkCore;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseLibRed("data/Blogging.accdb");
}
```

The connection string is the path to the database file — nothing else to configure, and no driver to match.
If the file does not exist, `EnsureCreated()` / `Migrate()` will create it.

## SQL modes

Because LibRed owns both the SQL generator and the engine that parses the result, it does not have to
restrict itself to what the Access dialect accepts. `UseLibRed` takes an optional `LibRedSqlMode`:

- **`Extended`** (the default) — LibRed's own SQL generator. It emits standard SQL, including constructs
  Access has no syntax for at all: `CROSS`/`OUTER APPLY`, window functions, `OFFSET`/`FETCH` paging,
  `FULL OUTER JOIN`, `CASE`, `COALESCE`, `NULLIF`, and set operations inside subquery predicates.
- **`Compatible`** — the same SQL the `EntityFrameworkCore.Jet` provider generates, so the statements would
  also run against the real Access engine. Use it when the same queries have to work against both.

Either way, a hand-written Access query behaves the way Access does. The mode changes what the provider
generates, not what the engine accepts.

## What works

Reading and writing every page type, full B-tree index maintenance, `CREATE`/`ALTER`/`DROP TABLE`, primary
and foreign keys with referential integrity and cascade actions, `DEFAULT` and `CHECK` constraints, calculated
columns (they map to EF Core's `ComputedColumnSql`, and scaffold back out of an existing database), views and
stored procedures written the way Access writes them, transactions with real commit and rollback, and text
index keys for both sort-order versions across the whole Basic Multilingual Plane plus the locale sort
orders.

Databases LibRed creates from nothing open cleanly in Access, in any format from Jet 4 (`.mdb`) through
ACE 17, defaulting to the Access 2007 format. Pointed at a database somebody else built, it reads the saved
queries too — an existing Access application's query layer shows up as views.

The ACE 16/17 types `BIGINT` and `DATETIME2` are supported, including raising a file's format version when
DDL introduces one — which is what Access itself does.

Encrypted databases are handled both ways: reading and writing Agile (AES-256-CBC/SHA-512), Office Standard
AES-256 and RC4, and the legacy Jet page encoding, plus setting, changing and removing passwords.

## Status and limitations

This is an **alpha**. It is used against Entity Framework Core's own specification suite, but it has not
been through production use.

- **Single writer.** LibRed tolerates extra open handles, but implements no multi-user concurrency control:
  no lock file, and none of the page/record locking Access coordinates through its side-car `.laccdb`/`.ldb`.
  Safe for any number of readers with no writer, or one writer with serialized access. Two concurrent writers
  will corrupt the file, so this is not the library for a shared network database.
- Jet 3 (Access 97) files are not supported; the Jet 4 / ACE family is.
- **Validation rules authored in the Access UI are read but not enforced.** They survive a round trip and are
  reported through `INFORMATION_SCHEMA`, but LibRed will accept a row that Access would have rejected. SQL
  `CHECK` constraints *are* enforced.

## Packages

`EntityFrameworkCore.LibRed` is the entry point and pulls in the rest:

| Package | Layer |
| --- | --- |
| `EntityFrameworkCore.LibRed` | EF Core provider |
| `LibRed.Ado` | ADO.NET surface — connection, command, reader, transaction |
| `LibRed.Engine` | Query planner and executor |
| `LibRed.Sql` | SQL front end — grammar, parser, binder |
| `LibRed.Core` | The `.mdb`/`.accdb` file format itself |

## Links

- [Source and issues](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet)
- [LibRed documentation](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/blob/master/src/LibRed/README.md)
- [On-disk format specification](https://github.com/CirrusRedOrg/EntityFrameworkCore.Jet/blob/master/src/LibRed/docs/format/README.md)

LibRed lives in the `EntityFrameworkCore.Jet` repository alongside the ODBC/OLE DB-based
[`EntityFrameworkCore.Jet`](https://www.nuget.org/packages/EntityFrameworkCore.Jet/) provider, which remains
the option to use when you want the real Access engine doing the work.
