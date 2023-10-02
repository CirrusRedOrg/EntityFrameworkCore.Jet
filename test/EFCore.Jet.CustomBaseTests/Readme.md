Due to a Jet/MS Access limitation, we need to use our own (slightly modified) version of the GarsOfWar tests

When working with a foreign key declaration over multiple fields, each field must meet the condition.
This is different to other databases where if any field is null, the foreign key constraint is not required
PostgreSql call this MATCH SIMPLE and MATCH FULL is where each field individually must meet the constraint
SQL Server operates in MATCH SIMPLE mode
Jet differs and operates in MATCH FULL mode

```
modelBuilder.Entity<Officer>().HasMany(o => o.Reports).WithOne().HasForeignKey(
            o => new { o.LeaderNickname, o.LeaderSquadId });
```

With the Officer class, the foreign key is defined over two fields, LeaderNickname and LeaderSquadId.
If either of these fields are null, the foreign key constraint is not enforced in SQL Server.
LeaderSquadId however is defined as NOT NULL in the database, so the default value is 0
When LeaderNickname is null, the foreign key constraint is not enforced in SQL Server, but is in Jet
and with having LeaderSquadId as 0, the foreign key constraint is not met and an exception is thrown

To work around this we can make LeaderSquadId nullable in the model and database.
It is a simple change to the Gear class (int to int?).
However we are not able to modify that class when it comes from EF Core itself in the form of a NuGet package.

Thus we need our own version of all the applicable model classes and all its dependencies.
This includes the DbContext, the Fixture bases and the test base classes.

It is easier to maintain if we use our own specific namespace for these classes.

IMPORTANT: As the tests in the FunctionalTests derive from our own version and not the EF Core version,
any updates to the base tests,models, fixtures will not be automatically picked up.

The files in CustomBaseTests will need to be manually synchronised with the EF Core version.

## Usage ##

If you need to switch into Match Simple mode rather than Match Full:
1. Add `.MatchSimple()` to the foreign key declaration
2. Ensure all fields in the foreign key are nullable


```
modelBuilder.Entity<Officer>().HasMany(o => o.Reports).WithOne().HasForeignKey(
            o => new { o.LeaderNickname, o.LeaderSquadId }).MatchSimple();
```

The reason this works is that although we have relaxed the foreign key constraint so that it can have all fields null,
which provides the option of having a row with a valid empty foreign key constraint. If any of the fields were not nullable,
it would be required to have the individual field meeting its constaint always. Hence there would always be a relationship to another row (and not an empty relationship).

Adding the `.MatchSimple` to the foreign key constraint, we are adding an annotation to the foreign key constraint.
When processing a join using that foreign key, we can check that annotation and if present,
we can add to the SQL some extra checks so asto behave like Match Simple mode. The SQL is basically to enforce the constraint of having all fields
from that foreign key on this join to not be null.

## Current Progress / Known Issues ##

- Code to add the Match Simple annotation to the foreign key is in place
- Code to add the extra constraint SQL to the join is NOT done yet

See the following tests which are affected by this issue:
 - Project_collection_navigation_with_inheritance2
 - Project_collection_navigation_with_inheritance3

In total I think there is only around 5 sets of tests that are affected by this issue (all in GearsOfWar, TPCGearsOfWar and TPTGearsOfWar)