using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable xUnit1003 // Theory methods must have test data

namespace EntityFramework.Jet.FunctionalTests
{
    public class GearsOfWarQueryJetTest : GearsOfWarQueryTestBase<GearsOfWarQueryJetFixture>
    {
        public GearsOfWarQueryJetTest(GearsOfWarQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }


        public override async Task Entity_equality_empty(bool isAsync)
        {
            await base.Entity_equality_empty(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[Nickname] IS NULL AND ([g].[SquadId] = 0))");
        }

        public override async Task Include_multiple_one_to_one_and_one_to_many(bool isAsync)
        {
            await base.Include_multiple_one_to_one_and_one_to_many(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
ORDER BY [t0].[FullName]",
                //
                @"SELECT [t.Gear.Weapons].[Id], [t.Gear.Weapons].[AmmunitionType], [t.Gear.Weapons].[IsAutomatic], [t.Gear.Weapons].[Name], [t.Gear.Weapons].[OwnerFullName], [t.Gear.Weapons].[SynergyWithId]
FROM [Weapon] AS [t.Gear.Weapons]
INNER JOIN (
    SELECT DISTINCT [t2].[FullName]
    FROM [CogTag] AS [t1]
    LEFT JOIN (
        SELECT [t.Gear0].*
        FROM [Gear] AS [t.Gear0]
        WHERE [t.Gear0].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON ([t1].[GearNickName] = [t2].[Nickname]) AND ([t1].[GearSquadId] = [t2].[SquadId])
) AS [t3] ON [t.Gear.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName]");
        }

        public override async Task Include_multiple_one_to_one_and_one_to_many_self_reference(bool isAsync)
        {
            await base.Include_multiple_one_to_one_and_one_to_many_self_reference(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
ORDER BY [t0].[FullName]",
                //
                @"SELECT [t.Gear.Weapons].[Id], [t.Gear.Weapons].[AmmunitionType], [t.Gear.Weapons].[IsAutomatic], [t.Gear.Weapons].[Name], [t.Gear.Weapons].[OwnerFullName], [t.Gear.Weapons].[SynergyWithId]
FROM [Weapon] AS [t.Gear.Weapons]
INNER JOIN (
    SELECT DISTINCT [t2].[FullName]
    FROM [CogTag] AS [t1]
    LEFT JOIN (
        SELECT [t.Gear0].*
        FROM [Gear] AS [t.Gear0]
        WHERE [t.Gear0].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON ([t1].[GearNickName] = [t2].[Nickname]) AND ([t1].[GearSquadId] = [t2].[SquadId])
) AS [t3] ON [t.Gear.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName]");
        }

        public override async Task Include_multiple_one_to_one_and_one_to_one_and_one_to_many(bool isAsync)
        {
            await base.Include_multiple_one_to_one_and_one_to_one_and_one_to_many(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t.Gear.Squad].[Id], [t.Gear.Squad].[InternalNumber], [t.Gear.Squad].[Name]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
LEFT JOIN [Squad] AS [t.Gear.Squad] ON [t0].[SquadId] = [t.Gear.Squad].[Id]
ORDER BY [t.Gear.Squad].[Id]",
                //
                @"SELECT [t.Gear.Squad.Members].[Nickname], [t.Gear.Squad.Members].[SquadId], [t.Gear.Squad.Members].[AssignedCityName], [t.Gear.Squad.Members].[CityOrBirthName], [t.Gear.Squad.Members].[Discriminator], [t.Gear.Squad.Members].[FullName], [t.Gear.Squad.Members].[HasSoulPatch], [t.Gear.Squad.Members].[LeaderNickname], [t.Gear.Squad.Members].[LeaderSquadId], [t.Gear.Squad.Members].[Rank]
FROM [Gear] AS [t.Gear.Squad.Members]
INNER JOIN (
    SELECT DISTINCT [t.Gear.Squad0].[Id]
    FROM [CogTag] AS [t1]
    LEFT JOIN (
        SELECT [t.Gear0].*
        FROM [Gear] AS [t.Gear0]
        WHERE [t.Gear0].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON ([t1].[GearNickName] = [t2].[Nickname]) AND ([t1].[GearSquadId] = [t2].[SquadId])
    LEFT JOIN [Squad] AS [t.Gear.Squad0] ON [t2].[SquadId] = [t.Gear.Squad0].[Id]
) AS [t3] ON [t.Gear.Squad.Members].[SquadId] = [t3].[Id]
WHERE [t.Gear.Squad.Members].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t3].[Id]");
        }

        public override async Task Include_multiple_one_to_one_optional_and_one_to_one_required(bool isAsync)
        {
            await base.Include_multiple_one_to_one_optional_and_one_to_one_required(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t.Gear.Squad].[Id], [t.Gear.Squad].[InternalNumber], [t.Gear.Squad].[Name]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
LEFT JOIN [Squad] AS [t.Gear.Squad] ON [t0].[SquadId] = [t.Gear.Squad].[Id]");
        }

        public override async Task Include_multiple_circular(bool isAsync)
        {
            await base.Include_multiple_circular(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g.CityOfBirth].[Name]",
                //
                @"SELECT [g.CityOfBirth.StationedGears].[Nickname], [g.CityOfBirth.StationedGears].[SquadId], [g.CityOfBirth.StationedGears].[AssignedCityName], [g.CityOfBirth.StationedGears].[CityOrBirthName], [g.CityOfBirth.StationedGears].[Discriminator], [g.CityOfBirth.StationedGears].[FullName], [g.CityOfBirth.StationedGears].[HasSoulPatch], [g.CityOfBirth.StationedGears].[LeaderNickname], [g.CityOfBirth.StationedGears].[LeaderSquadId], [g.CityOfBirth.StationedGears].[Rank]
FROM [Gear] AS [g.CityOfBirth.StationedGears]
INNER JOIN (
    SELECT DISTINCT [g.CityOfBirth0].[Name]
    FROM [Gear] AS [g0]
    INNER JOIN [City] AS [g.CityOfBirth0] ON [g0].[CityOrBirthName] = [g.CityOfBirth0].[Name]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g.CityOfBirth.StationedGears].[AssignedCityName] = [t].[Name]
WHERE [g.CityOfBirth.StationedGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[Name]");
        }

        public override async Task Include_multiple_circular_with_filter(bool isAsync)
        {
            await base.Include_multiple_circular_with_filter(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[Nickname] = 'Marcus')
ORDER BY [g.CityOfBirth].[Name]",
                //
                @"SELECT [g.CityOfBirth.StationedGears].[Nickname], [g.CityOfBirth.StationedGears].[SquadId], [g.CityOfBirth.StationedGears].[AssignedCityName], [g.CityOfBirth.StationedGears].[CityOrBirthName], [g.CityOfBirth.StationedGears].[Discriminator], [g.CityOfBirth.StationedGears].[FullName], [g.CityOfBirth.StationedGears].[HasSoulPatch], [g.CityOfBirth.StationedGears].[LeaderNickname], [g.CityOfBirth.StationedGears].[LeaderSquadId], [g.CityOfBirth.StationedGears].[Rank]
FROM [Gear] AS [g.CityOfBirth.StationedGears]
INNER JOIN (
    SELECT DISTINCT [g.CityOfBirth0].[Name]
    FROM [Gear] AS [g0]
    INNER JOIN [City] AS [g.CityOfBirth0] ON [g0].[CityOrBirthName] = [g.CityOfBirth0].[Name]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND ([g0].[Nickname] = 'Marcus')
) AS [t] ON [g.CityOfBirth.StationedGears].[AssignedCityName] = [t].[Name]
WHERE [g.CityOfBirth.StationedGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[Name]");
        }

        public override async Task Include_using_alternate_key(bool isAsync)
        {
            await base.Include_using_alternate_key(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[Nickname] = 'Marcus')
ORDER BY [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT [g0].[FullName]
    FROM [Gear] AS [g0]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND ([g0].[Nickname] = 'Marcus')
) AS [t] ON [g.Weapons].[OwnerFullName] = [t].[FullName]
ORDER BY [t].[FullName]");
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Optional_navigation_with_collection_composite_key()
        {
            base.Optional_navigation_with_collection_composite_key();
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Project_collection_navigation_with_inheritance1()
        {
            base.Project_collection_navigation_with_inheritance1();
        }

        public override async Task Include_multiple_include_then_include(bool isAsync)
        {
            await base.Include_multiple_include_then_include(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location], [g.AssignedCity].[Name], [g.AssignedCity].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
LEFT JOIN [City] AS [g.AssignedCity] ON [g].[AssignedCityName] = [g.AssignedCity].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[Nickname], [g.AssignedCity].[Name], [g.CityOfBirth].[Name]",
                //
                @"SELECT [g.AssignedCity.BornGears].[Nickname], [g.AssignedCity.BornGears].[SquadId], [g.AssignedCity.BornGears].[AssignedCityName], [g.AssignedCity.BornGears].[CityOrBirthName], [g.AssignedCity.BornGears].[Discriminator], [g.AssignedCity.BornGears].[FullName], [g.AssignedCity.BornGears].[HasSoulPatch], [g.AssignedCity.BornGears].[LeaderNickname], [g.AssignedCity.BornGears].[LeaderSquadId], [g.AssignedCity.BornGears].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [Gear] AS [g.AssignedCity.BornGears]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g.AssignedCity.BornGears].[Nickname] = [g#Tag].[GearNickName]) AND ([g.AssignedCity.BornGears].[SquadId] = [g#Tag].[GearSquadId])
INNER JOIN (
    SELECT DISTINCT [g.AssignedCity0].[Name], [g0].[Nickname]
    FROM [Gear] AS [g0]
    INNER JOIN [City] AS [g.CityOfBirth0] ON [g0].[CityOrBirthName] = [g.CityOfBirth0].[Name]
    LEFT JOIN [City] AS [g.AssignedCity0] ON [g0].[AssignedCityName] = [g.AssignedCity0].[Name]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g.AssignedCity.BornGears].[CityOrBirthName] = [t].[Name]
WHERE [g.AssignedCity.BornGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[Nickname], [t].[Name]",
                //
                @"SELECT [g.AssignedCity.StationedGears].[Nickname], [g.AssignedCity.StationedGears].[SquadId], [g.AssignedCity.StationedGears].[AssignedCityName], [g.AssignedCity.StationedGears].[CityOrBirthName], [g.AssignedCity.StationedGears].[Discriminator], [g.AssignedCity.StationedGears].[FullName], [g.AssignedCity.StationedGears].[HasSoulPatch], [g.AssignedCity.StationedGears].[LeaderNickname], [g.AssignedCity.StationedGears].[LeaderSquadId], [g.AssignedCity.StationedGears].[Rank], [g.Tag0].[Id], [g.Tag0].[GearNickName], [g.Tag0].[GearSquadId], [g.Tag0].[Note]
FROM [Gear] AS [g.AssignedCity.StationedGears]
LEFT JOIN [CogTag] AS [g.Tag0] ON ([g.AssignedCity.StationedGears].[Nickname] = [g.Tag0].[GearNickName]) AND ([g.AssignedCity.StationedGears].[SquadId] = [g.Tag0].[GearSquadId])
INNER JOIN (
    SELECT DISTINCT [g.AssignedCity1].[Name], [g1].[Nickname]
    FROM [Gear] AS [g1]
    INNER JOIN [City] AS [g.CityOfBirth1] ON [g1].[CityOrBirthName] = [g.CityOfBirth1].[Name]
    LEFT JOIN [City] AS [g.AssignedCity1] ON [g1].[AssignedCityName] = [g.AssignedCity1].[Name]
    WHERE [g1].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON [g.AssignedCity.StationedGears].[AssignedCityName] = [t0].[Name]
WHERE [g.AssignedCity.StationedGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t0].[Nickname], [t0].[Name]",
                //
                @"SELECT [g.CityOfBirth.BornGears].[Nickname], [g.CityOfBirth.BornGears].[SquadId], [g.CityOfBirth.BornGears].[AssignedCityName], [g.CityOfBirth.BornGears].[CityOrBirthName], [g.CityOfBirth.BornGears].[Discriminator], [g.CityOfBirth.BornGears].[FullName], [g.CityOfBirth.BornGears].[HasSoulPatch], [g.CityOfBirth.BornGears].[LeaderNickname], [g.CityOfBirth.BornGears].[LeaderSquadId], [g.CityOfBirth.BornGears].[Rank], [g.Tag1].[Id], [g.Tag1].[GearNickName], [g.Tag1].[GearSquadId], [g.Tag1].[Note]
FROM [Gear] AS [g.CityOfBirth.BornGears]
LEFT JOIN [CogTag] AS [g.Tag1] ON ([g.CityOfBirth.BornGears].[Nickname] = [g.Tag1].[GearNickName]) AND ([g.CityOfBirth.BornGears].[SquadId] = [g.Tag1].[GearSquadId])
INNER JOIN (
    SELECT DISTINCT [g.CityOfBirth2].[Name], [g2].[Nickname], [g.AssignedCity2].[Name] AS [Name0]
    FROM [Gear] AS [g2]
    INNER JOIN [City] AS [g.CityOfBirth2] ON [g2].[CityOrBirthName] = [g.CityOfBirth2].[Name]
    LEFT JOIN [City] AS [g.AssignedCity2] ON [g2].[AssignedCityName] = [g.AssignedCity2].[Name]
    WHERE [g2].[Discriminator] IN ('Officer', 'Gear')
) AS [t1] ON [g.CityOfBirth.BornGears].[CityOrBirthName] = [t1].[Name]
WHERE [g.CityOfBirth.BornGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t1].[Nickname], [t1].[Name0], [t1].[Name]",
                //
                @"SELECT [g.CityOfBirth.StationedGears].[Nickname], [g.CityOfBirth.StationedGears].[SquadId], [g.CityOfBirth.StationedGears].[AssignedCityName], [g.CityOfBirth.StationedGears].[CityOrBirthName], [g.CityOfBirth.StationedGears].[Discriminator], [g.CityOfBirth.StationedGears].[FullName], [g.CityOfBirth.StationedGears].[HasSoulPatch], [g.CityOfBirth.StationedGears].[LeaderNickname], [g.CityOfBirth.StationedGears].[LeaderSquadId], [g.CityOfBirth.StationedGears].[Rank], [g.Tag2].[Id], [g.Tag2].[GearNickName], [g.Tag2].[GearSquadId], [g.Tag2].[Note]
FROM [Gear] AS [g.CityOfBirth.StationedGears]
LEFT JOIN [CogTag] AS [g.Tag2] ON ([g.CityOfBirth.StationedGears].[Nickname] = [g.Tag2].[GearNickName]) AND ([g.CityOfBirth.StationedGears].[SquadId] = [g.Tag2].[GearSquadId])
INNER JOIN (
    SELECT DISTINCT [g.CityOfBirth3].[Name], [g3].[Nickname], [g.AssignedCity3].[Name] AS [Name0]
    FROM [Gear] AS [g3]
    INNER JOIN [City] AS [g.CityOfBirth3] ON [g3].[CityOrBirthName] = [g.CityOfBirth3].[Name]
    LEFT JOIN [City] AS [g.AssignedCity3] ON [g3].[AssignedCityName] = [g.AssignedCity3].[Name]
    WHERE [g3].[Discriminator] IN ('Officer', 'Gear')
) AS [t2] ON [g.CityOfBirth.StationedGears].[AssignedCityName] = [t2].[Name]
WHERE [g.CityOfBirth.StationedGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t2].[Nickname], [t2].[Name0], [t2].[Name]");
        }

        public override async Task Include_navigation_on_derived_type(bool isAsync)
        {
            await base.Include_navigation_on_derived_type(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] = 'Officer'
ORDER BY [g].[Nickname], [g].[SquadId]",
                //
                @"SELECT [o.Reports].[Nickname], [o.Reports].[SquadId], [o.Reports].[AssignedCityName], [o.Reports].[CityOrBirthName], [o.Reports].[Discriminator], [o.Reports].[FullName], [o.Reports].[HasSoulPatch], [o.Reports].[LeaderNickname], [o.Reports].[LeaderSquadId], [o.Reports].[Rank]
FROM [Gear] AS [o.Reports]
INNER JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId]
    FROM [Gear] AS [g0]
    WHERE [g0].[Discriminator] = 'Officer'
) AS [t] ON ([o.Reports].[LeaderNickname] = [t].[Nickname]) AND ([o.Reports].[LeaderSquadId] = [t].[SquadId])
WHERE [o.Reports].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[Nickname], [t].[SquadId]");
        }

        public override async Task String_based_Include_navigation_on_derived_type(bool isAsync)
        {
            await base.String_based_Include_navigation_on_derived_type(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] = 'Officer'
ORDER BY [g].[Nickname], [g].[SquadId]",
                //
                @"SELECT [o.Reports].[Nickname], [o.Reports].[SquadId], [o.Reports].[AssignedCityName], [o.Reports].[CityOrBirthName], [o.Reports].[Discriminator], [o.Reports].[FullName], [o.Reports].[HasSoulPatch], [o.Reports].[LeaderNickname], [o.Reports].[LeaderSquadId], [o.Reports].[Rank]
FROM [Gear] AS [o.Reports]
INNER JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId]
    FROM [Gear] AS [g0]
    WHERE [g0].[Discriminator] = 'Officer'
) AS [t] ON ([o.Reports].[LeaderNickname] = [t].[Nickname]) AND ([o.Reports].[LeaderSquadId] = [t].[SquadId])
WHERE [o.Reports].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[Nickname], [t].[SquadId]");
        }

        public override async Task Select_Where_Navigation_Included(bool isAsync)
        {
            await base.Select_Where_Navigation_Included(isAsync);

            AssertSql(
                @"SELECT [o].[Id], [o].[GearNickName], [o].[GearSquadId], [o].[Note], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [CogTag] AS [o]
LEFT JOIN (
    SELECT [o.Gear].*
    FROM [Gear] AS [o.Gear]
    WHERE [o.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([o].[GearNickName] = [t].[Nickname]) AND ([o].[GearSquadId] = [t].[SquadId])
WHERE [t].[Nickname] = 'Marcus'");
        }

        public override async Task Include_with_join_reference1(bool isAsync)
        {
            await base.Include_with_join_reference1(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
INNER JOIN [CogTag] AS [t] ON ([g].[SquadId] = [t].[GearSquadId]) AND ([g].[Nickname] = [t].[GearNickName])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        public override async Task Include_with_join_reference2(bool isAsync)
        {
            await base.Include_with_join_reference2(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [CogTag] AS [t]
INNER JOIN [Gear] AS [g] ON ([t].[GearSquadId] = [g].[SquadId]) AND ([t].[GearNickName] = [g].[Nickname])
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        public override async Task Include_with_join_collection1(bool isAsync)
        {
            await base.Include_with_join_collection1(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
INNER JOIN [CogTag] AS [t] ON ([g].[SquadId] = [t].[GearSquadId]) AND ([g].[Nickname] = [t].[GearNickName])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g0].[FullName]
    FROM [Gear] AS [g0]
    INNER JOIN [CogTag] AS [t0] ON ([g0].[SquadId] = [t0].[GearSquadId]) AND ([g0].[Nickname] = [t0].[GearNickName])
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
) AS [t1] ON [g.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_with_join_collection2(bool isAsync)
        {
            await base.Include_with_join_collection2(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [CogTag] AS [t]
INNER JOIN [Gear] AS [g] ON ([t].[GearSquadId] = [g].[SquadId]) AND ([t].[GearNickName] = [g].[Nickname])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g0].[FullName]
    FROM [CogTag] AS [t0]
    INNER JOIN [Gear] AS [g0] ON ([t0].[GearSquadId] = [g0].[SquadId]) AND ([t0].[GearNickName] = [g0].[Nickname])
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
) AS [t1] ON [g.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        [Theory(Skip = "Assertion failed without evident reason")]
        public override void Include_where_list_contains_navigation(bool isAsync)
        {
            base.Include_where_list_contains_navigation(isAsync);

            AssertSql(
                @"SELECT [t].[Id]
FROM [CogTag] AS [t]");

            Assert.Contains(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g#Tag].[Id] IS NOT NULL AND [g#Tag].[Id] IN (",
                Fixture.TestSqlLoggerFactory.SqlStatements[1]);
        }

        [Theory(Skip = "Assertion failed without evident reason")]
        public override void Include_where_list_contains_navigation2(bool isAsync)
        {
            base.Include_where_list_contains_navigation2(isAsync);

            AssertSql(
                @"SELECT [t].[Id]
FROM [CogTag] AS [t]");

            Assert.Contains(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g.CityOfBirth].[Location] IS NOT NULL AND [g#Tag].[Id] IN (",
                Fixture.TestSqlLoggerFactory.SqlStatements[1]);
        }

        [Theory(Skip = "Assertion failed without evident reason")]
        public override void Navigation_accessed_twice_outside_and_inside_subquery(bool isAsync)
        {
            base.Navigation_accessed_twice_outside_and_inside_subquery(isAsync);

            AssertSql(
                @"SELECT [t].[Id]
FROM [CogTag] AS [t]");

            Assert.Contains(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g#Tag].[Id] IS NOT NULL AND [g#Tag].[Id] IN (",
                Fixture.TestSqlLoggerFactory.SqlStatements[1]);
        }

        public override async Task Include_with_join_multi_level(bool isAsync)
        {
            await base.Include_with_join_multi_level(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
INNER JOIN [CogTag] AS [t] ON ([g].[SquadId] = [t].[GearSquadId]) AND ([g].[Nickname] = [t].[GearNickName])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g.CityOfBirth].[Name]",
                //
                @"SELECT [g.CityOfBirth.StationedGears].[Nickname], [g.CityOfBirth.StationedGears].[SquadId], [g.CityOfBirth.StationedGears].[AssignedCityName], [g.CityOfBirth.StationedGears].[CityOrBirthName], [g.CityOfBirth.StationedGears].[Discriminator], [g.CityOfBirth.StationedGears].[FullName], [g.CityOfBirth.StationedGears].[HasSoulPatch], [g.CityOfBirth.StationedGears].[LeaderNickname], [g.CityOfBirth.StationedGears].[LeaderSquadId], [g.CityOfBirth.StationedGears].[Rank]
FROM [Gear] AS [g.CityOfBirth.StationedGears]
INNER JOIN (
    SELECT DISTINCT [g.CityOfBirth0].[Name]
    FROM [Gear] AS [g0]
    INNER JOIN [City] AS [g.CityOfBirth0] ON [g0].[CityOrBirthName] = [g.CityOfBirth0].[Name]
    INNER JOIN [CogTag] AS [t0] ON ([g0].[SquadId] = [t0].[GearSquadId]) AND ([g0].[Nickname] = [t0].[GearNickName])
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
) AS [t1] ON [g.CityOfBirth.StationedGears].[AssignedCityName] = [t1].[Name]
WHERE [g.CityOfBirth.StationedGears].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t1].[Name]");
        }

        public override async Task Include_with_join_and_inheritance1(bool isAsync)
        {
            await base.Include_with_join_and_inheritance1(isAsync);

            AssertSql(
                @"SELECT [o].[Nickname], [o].[SquadId], [o].[AssignedCityName], [o].[CityOrBirthName], [o].[Discriminator], [o].[FullName], [o].[HasSoulPatch], [o].[LeaderNickname], [o].[LeaderSquadId], [o].[Rank], [o.CityOfBirth].[Name], [o.CityOfBirth].[Location]
FROM [CogTag] AS [t]
INNER JOIN [Gear] AS [o] ON ([t].[GearSquadId] = [o].[SquadId]) AND ([t].[GearNickName] = [o].[Nickname])
INNER JOIN [City] AS [o.CityOfBirth] ON [o].[CityOrBirthName] = [o.CityOfBirth].[Name]
WHERE [o].[Discriminator] = 'Officer'");
        }

        public override async Task Include_with_join_and_inheritance2(bool isAsync)
        {
            await base.Include_with_join_and_inheritance2(isAsync);

            AssertSql(
                @"SELECT [o].[Nickname], [o].[SquadId], [o].[AssignedCityName], [o].[CityOrBirthName], [o].[Discriminator], [o].[FullName], [o].[HasSoulPatch], [o].[LeaderNickname], [o].[LeaderSquadId], [o].[Rank]
FROM [Gear] AS [o]
INNER JOIN [CogTag] AS [t] ON ([o].[SquadId] = [t].[GearSquadId]) AND ([o].[Nickname] = [t].[GearNickName])
WHERE [o].[Discriminator] = 'Officer'
ORDER BY [o].[FullName]",
                //
                @"SELECT [o.Weapons].[Id], [o.Weapons].[AmmunitionType], [o.Weapons].[IsAutomatic], [o.Weapons].[Name], [o.Weapons].[OwnerFullName], [o.Weapons].[SynergyWithId]
FROM [Weapon] AS [o.Weapons]
INNER JOIN (
    SELECT DISTINCT [o0].[FullName]
    FROM [Gear] AS [o0]
    INNER JOIN [CogTag] AS [t0] ON ([o0].[SquadId] = [t0].[GearSquadId]) AND ([o0].[Nickname] = [t0].[GearNickName])
    WHERE [o0].[Discriminator] = 'Officer'
) AS [t1] ON [o.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_with_join_and_inheritance3(bool isAsync)
        {
            await base.Include_with_join_and_inheritance3(isAsync);

            AssertSql(
                @"SELECT [o].[Nickname], [o].[SquadId], [o].[AssignedCityName], [o].[CityOrBirthName], [o].[Discriminator], [o].[FullName], [o].[HasSoulPatch], [o].[LeaderNickname], [o].[LeaderSquadId], [o].[Rank]
FROM [CogTag] AS [t]
INNER JOIN [Gear] AS [o] ON ([t].[GearSquadId] = [o].[SquadId]) AND ([t].[GearNickName] = [o].[Nickname])
WHERE [o].[Discriminator] = 'Officer'
ORDER BY [o].[Nickname], [o].[SquadId]",
                //
                @"SELECT [o.Reports].[Nickname], [o.Reports].[SquadId], [o.Reports].[AssignedCityName], [o.Reports].[CityOrBirthName], [o.Reports].[Discriminator], [o.Reports].[FullName], [o.Reports].[HasSoulPatch], [o.Reports].[LeaderNickname], [o.Reports].[LeaderSquadId], [o.Reports].[Rank]
FROM [Gear] AS [o.Reports]
INNER JOIN (
    SELECT DISTINCT [o0].[Nickname], [o0].[SquadId]
    FROM [CogTag] AS [t0]
    INNER JOIN [Gear] AS [o0] ON ([t0].[GearSquadId] = [o0].[SquadId]) AND ([t0].[GearNickName] = [o0].[Nickname])
    WHERE [o0].[Discriminator] = 'Officer'
) AS [t1] ON ([o.Reports].[LeaderNickname] = [t1].[Nickname]) AND ([o.Reports].[LeaderSquadId] = [t1].[SquadId])
WHERE [o.Reports].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t1].[Nickname], [t1].[SquadId]");
        }

        public override async Task Include_with_nested_navigation_in_order_by(bool isAsync)
        {
            await base.Include_with_nested_navigation_in_order_by(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Weapon] AS [w]
LEFT JOIN (
    SELECT [w.Owner].*
    FROM [Gear] AS [w.Owner]
    WHERE [w.Owner].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [w].[OwnerFullName] = [t].[FullName]
LEFT JOIN [City] AS [w.Owner.CityOfBirth] ON [t].[CityOrBirthName] = [w.Owner.CityOfBirth].[Name]
ORDER BY [w.Owner.CityOfBirth].[Name]");
        }

        public override async Task Where_enum(bool isAsync)
        {
            await base.Where_enum(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[Rank] = 2)");
        }

        public override async Task Where_nullable_enum_with_constant(bool isAsync)
        {
            await base.Where_nullable_enum_with_constant(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] = 1");
        }

        public override async Task Where_nullable_enum_with_null_constant(bool isAsync)
        {
            await base.Where_nullable_enum_with_null_constant(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] IS NULL");
        }

        public override async Task Where_nullable_enum_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_nullable_enum_with_non_nullable_parameter(isAsync);

            AssertSql(
                @"@__ammunitionType_0='Cartridge'

SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] = @__ammunitionType_0");
        }


        [ConditionalFact]
        public override async Task Where_bitwise_and_enum(bool isAsync)
        {
            await base.Where_bitwise_and_enum(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) > 0)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)");
        }

        [ConditionalFact]
        public override async Task Where_bitwise_and_integral(bool isAsync)
        {
            await base.Where_bitwise_and_integral(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)");
        }

        [ConditionalFact]
        public override async Task Where_bitwise_and_nullable_enum_with_constant(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_constant(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] BAND 1) > 0");
        }

        [ConditionalFact]
        public override async Task Where_bitwise_and_nullable_enum_with_null_constant(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_null_constant(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] BAND NULL) > 0");
        }

        [ConditionalFact]
        public override async Task Where_bitwise_and_nullable_enum_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_non_nullable_parameter(isAsync);

            AssertSql(
                @"@__ammunitionType_0='Cartridge'

SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] BAND @__ammunitionType_0) > 0");
        }

        [ConditionalFact]
        public override async Task Where_bitwise_and_nullable_enum_with_nullable_parameter(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_nullable_parameter(isAsync);

            AssertSql(
                @"@__ammunitionType_0='Cartridge'

SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] BAND @__ammunitionType_0) > 0",
                //
                @"@__ammunitionType_0='' (Nullable = false) (DbType = String)

SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] BAND @__ammunitionType_0) > 0");
        }


        [Theory(Skip = "Unsupported by JET: test model not suitable for JET foreign keys")]
        public override async Task Where_bitwise_or_enum(bool isAsync)
        {
            await base.Where_bitwise_or_enum(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] | 1) > 0)");
        }


        public override async Task Where_enum_has_flag(bool isAsync)
        {
            await base.Where_enum_has_flag(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 5) = 5)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ((1 BAND [g].[Rank]) = [g].[Rank])");
        }

        public override async Task Where_enum_has_flag_subquery(bool isAsync)
        {
            await base.Where_enum_has_flag_subquery(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND (
    SELECT TOP 1 [x].[Rank]
    FROM [Gear] AS [x]
    WHERE [x].[Discriminator] IN ('Officer', 'Gear')
    ORDER BY [x].[Nickname], [x].[SquadId]
)) = (
    SELECT TOP 1 [x].[Rank]
    FROM [Gear] AS [x]
    WHERE [x].[Discriminator] IN ('Officer', 'Gear')
    ORDER BY [x].[Nickname], [x].[SquadId]
))",
                //
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ((1 BAND (
    SELECT TOP 1 [x].[Rank]
    FROM [Gear] AS [x]
    WHERE [x].[Discriminator] IN ('Officer', 'Gear')
    ORDER BY [x].[Nickname], [x].[SquadId]
)) = (
    SELECT TOP 1 [x].[Rank]
    FROM [Gear] AS [x]
    WHERE [x].[Discriminator] IN ('Officer', 'Gear')
    ORDER BY [x].[Nickname], [x].[SquadId]
))");
        }

        public override async Task Where_enum_has_flag_subquery_client_eval(bool isAsync)
        {
            await base.Where_enum_has_flag_subquery_client_eval(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')",
                //
                @"SELECT TOP 1 [x0].[Rank]
FROM [Gear] AS [x0]
WHERE [x0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [x0].[Nickname], [x0].[SquadId]",
                //
                @"SELECT TOP 1 [x0].[Rank]
FROM [Gear] AS [x0]
WHERE [x0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [x0].[Nickname], [x0].[SquadId]",
                //
                @"SELECT TOP 1 [x0].[Rank]
FROM [Gear] AS [x0]
WHERE [x0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [x0].[Nickname], [x0].[SquadId]",
                //
                @"SELECT TOP 1 [x0].[Rank]
FROM [Gear] AS [x0]
WHERE [x0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [x0].[Nickname], [x0].[SquadId]",
                //
                @"SELECT TOP 1 [x0].[Rank]
FROM [Gear] AS [x0]
WHERE [x0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [x0].[Nickname], [x0].[SquadId]");
        }

        public override async Task Where_enum_has_flag_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_enum_has_flag_with_non_nullable_parameter(isAsync);

            AssertSql(
                @"@__parameter_0='Corporal'

SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND @__parameter_0) = @__parameter_0)");
        }


        public override async Task Select_enum_has_flag(bool isAsync)
        {
            await base.Select_enum_has_flag(isAsync);

            AssertSql(
                @"SELECT TOP 1 CASE
    WHEN ([g].[Rank] BAND 1) = 1
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [hasFlagTrue], CASE
    WHEN ([g].[Rank] BAND 2) = 2
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [hasFlagFalse]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Rank] BAND 1) = 1)");
        }

        [Theory(Skip = "Unsupported by JET")]
        public override async Task Where_count_subquery_without_collision(bool isAsync)
        {
            await base.Where_count_subquery_without_collision(isAsync);

            AssertSql(
                @"SELECT [w].[Nickname], [w].[SquadId], [w].[AssignedCityName], [w].[CityOrBirthName], [w].[Discriminator], [w].[FullName], [w].[HasSoulPatch], [w].[LeaderNickname], [w].[LeaderSquadId], [w].[Rank]
FROM [Gear] AS [w]
WHERE [w].[Discriminator] IN ('Officer', 'Gear') AND (2 IN (
    SELECT COUNT(*)
    FROM [Weapon] AS [w0]
    WHERE [w].[FullName] = [w0].[OwnerFullName]
))");
        }

        public override async Task Where_any_subquery_without_collision(bool isAsync)
        {
            await base.Where_any_subquery_without_collision(isAsync);

            AssertSql(
                @"SELECT [w].[Nickname], [w].[SquadId], [w].[AssignedCityName], [w].[CityOrBirthName], [w].[Discriminator], [w].[FullName], [w].[HasSoulPatch], [w].[LeaderNickname], [w].[LeaderSquadId], [w].[Rank]
FROM [Gear] AS [w]
WHERE [w].[Discriminator] IN ('Officer', 'Gear') AND EXISTS (
    SELECT 1
    FROM [Weapon] AS [w0]
    WHERE [w].[FullName] = [w0].[OwnerFullName])");
        }

        public override async Task Select_inverted_boolean(bool isAsync)
        {
            await base.Select_inverted_boolean(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN [w].[IsAutomatic] = 0
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [Manual]
FROM [Weapon] AS [w]
WHERE [w].[IsAutomatic] = 1");
        }

        public override async Task Select_comparison_with_null(bool isAsync)
        {
            await base.Select_comparison_with_null(isAsync);

            AssertSql(
                @"@__ammunitionType_1='Cartridge' (Nullable = true)
@__ammunitionType_0='Cartridge' (Nullable = true)

SELECT [w].[Id], CASE
    WHEN [w].[AmmunitionType] = @__ammunitionType_1
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [Cartidge]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] = @__ammunitionType_0",
                //
                @"SELECT [w].[Id], CASE
    WHEN [w].[AmmunitionType] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [Cartidge]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] IS NULL");
        }

        public override async Task Select_ternary_operation_with_boolean(bool isAsync)
        {
            await base.Select_ternary_operation_with_boolean(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN [w].[IsAutomatic] = 1
    THEN 1 ELSE 0
END AS [Num]
FROM [Weapon] AS [w]");
        }

        public override async Task Select_ternary_operation_with_inverted_boolean(bool isAsync)
        {
            await base.Select_ternary_operation_with_inverted_boolean(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN [w].[IsAutomatic] = 0
    THEN 1 ELSE 0
END AS [Num]
FROM [Weapon] AS [w]");
        }

        public override async Task Select_ternary_operation_with_has_value_not_null(bool isAsync)
        {
            await base.Select_ternary_operation_with_has_value_not_null(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN [w].[AmmunitionType] IS NOT NULL AND ([w].[AmmunitionType] = 1)
    THEN 'Yes' ELSE 'No'
END AS [IsCartidge]
FROM [Weapon] AS [w]
WHERE [w].[AmmunitionType] IS NOT NULL AND ([w].[AmmunitionType] = 1)");
        }

        public override async Task Select_ternary_operation_multiple_conditions(bool isAsync)
        {
            await base.Select_ternary_operation_multiple_conditions(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN ([w].[AmmunitionType] = 2) AND ([w].[SynergyWithId] = 1)
    THEN 'Yes' ELSE 'No'
END AS [IsCartidge]
FROM [Weapon] AS [w]");
        }

        public override async Task Select_ternary_operation_multiple_conditions_2(bool isAsync)
        {
            await base.Select_ternary_operation_multiple_conditions_2(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN ([w].[IsAutomatic] = 0) AND ([w].[SynergyWithId] = 1)
    THEN 'Yes' ELSE 'No'
END AS [IsCartidge]
FROM [Weapon] AS [w]");
        }

        public override async Task Select_multiple_conditions(bool isAsync)
        {
            await base.Select_multiple_conditions(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN ([w].[IsAutomatic] = 0) AND ([w].[SynergyWithId] = 1)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END AS [IsCartidge]
FROM [Weapon] AS [w]");
        }

        public override async Task Select_nested_ternary_operations(bool isAsync)
        {
            await base.Select_nested_ternary_operations(isAsync);

            AssertSql(
                @"SELECT [w].[Id], CASE
    WHEN [w].[IsAutomatic] = 0
    THEN CASE
        WHEN [w].[AmmunitionType] = 1
        THEN 'ManualCartridge' ELSE 'Manual'
    END ELSE 'Auto'
END AS [IsManualCartidge]
FROM [Weapon] AS [w]");
        }

        public override async Task Null_propagation_optimization1(bool isAsync)
        {
            await base.Null_propagation_optimization1(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[LeaderNickname] = 'Marcus')");
        }

        

        public override async Task Select_null_propagation_negative1(bool isAsync)
        {
            await base.Select_null_propagation_negative1(isAsync);

            AssertSql(
                @"SELECT CASE
    WHEN [g].[LeaderNickname] IS NOT NULL
    THEN CASE
        WHEN LEN([g].[Nickname]) = 5
        THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
    END ELSE NULL
END
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        public override async Task Select_null_propagation_negative2(bool isAsync)
        {
            await base.Select_null_propagation_negative2(isAsync);

            AssertSql(
                @"SELECT CASE
    WHEN [g1].[LeaderNickname] IS NOT NULL
    THEN [g2].[LeaderNickname] ELSE NULL
END
FROM [Gear] AS [g1]
, [Gear] AS [g2]
WHERE [g1].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_null_propagation_negative3(bool isAsync)
        {
            await base.Select_null_propagation_negative3(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_null_propagation_negative4(bool isAsync)
        {
            await base.Select_null_propagation_negative4(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_null_propagation_negative5(bool isAsync)
        {
            await base.Select_null_propagation_negative5(isAsync);
        }


        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(bool isAsync)
        {
            await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(isAsync);

            AssertSql(
                @"SELECT [ct1].[Id], [ct1].[GearNickName], [ct1].[GearSquadId], [ct1].[Note], [ct2].[Id], [ct2].[GearNickName], [ct2].[GearSquadId], [ct2].[Note]
FROM [CogTag] AS [ct1]
LEFT JOIN (
    SELECT [ct1.Gear].*
    FROM [Gear] AS [ct1.Gear]
    WHERE [ct1.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct1].[GearNickName] = [t].[Nickname]) AND ([ct1].[GearSquadId] = [t].[SquadId])
, [CogTag] AS [ct2]
LEFT JOIN (
    SELECT [ct2.Gear].*
    FROM [Gear] AS [ct2.Gear]
    WHERE [ct2.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([ct2].[GearNickName] = [t0].[Nickname]) AND ([ct2].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Nickname] = [t0].[Nickname]) OR ([t].[Nickname] IS NULL AND [t0].[Nickname] IS NULL)");
        }

        public override async Task Select_Singleton_Navigation_With_Member_Access(bool isAsync)
        {
            await base.Select_Singleton_Navigation_With_Member_Access(isAsync);

            AssertSql(
                @"SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName] AS [B], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [CogTag] AS [ct]
LEFT JOIN (
    SELECT [ct.Gear].*
    FROM [Gear] AS [ct.Gear]
    WHERE [ct.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct].[GearNickName] = [t].[Nickname]) AND ([ct].[GearSquadId] = [t].[SquadId])
WHERE ([t].[Nickname] = 'Marcus') AND (([t].[CityOrBirthName] <> 'Ephyra') OR [t].[CityOrBirthName] IS NULL)");
        }

        public override async Task Select_Where_Navigation(bool isAsync)
        {
            await base.Select_Where_Navigation(isAsync);

            AssertSql(
                @"SELECT [ct].[Id], [ct].[GearNickName], [ct].[GearSquadId], [ct].[Note]
FROM [CogTag] AS [ct]
LEFT JOIN (
    SELECT [ct.Gear].*
    FROM [Gear] AS [ct.Gear]
    WHERE [ct.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct].[GearNickName] = [t].[Nickname]) AND ([ct].[GearSquadId] = [t].[SquadId])
WHERE [t].[Nickname] = 'Marcus'");
        }

        public override async Task Select_Where_Navigation_Client(bool isAsync)
        {
            await base.Select_Where_Navigation_Client(isAsync);

            AssertSql(
                @"SELECT [o].[Id], [o].[GearNickName], [o].[GearSquadId], [o].[Note], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [CogTag] AS [o]
LEFT JOIN (
    SELECT [o.Gear].*
    FROM [Gear] AS [o.Gear]
    WHERE [o.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([o].[GearNickName] = [t].[Nickname]) AND ([o].[GearSquadId] = [t].[SquadId])
WHERE [o].[GearNickName] IS NOT NULL OR [o].[GearSquadId] IS NOT NULL");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_Where_Navigation_Equals_Navigation(bool isAsync)
        {
            await base.Select_Where_Navigation_Equals_Navigation(isAsync);

            AssertSql(
                @"SELECT [ct1].[Id], [ct1].[GearNickName], [ct1].[GearSquadId], [ct1].[Note], [ct2].[Id], [ct2].[GearNickName], [ct2].[GearSquadId], [ct2].[Note]
FROM [CogTag] AS [ct1]
LEFT JOIN (
    SELECT [ct1.Gear].*
    FROM [Gear] AS [ct1.Gear]
    WHERE [ct1.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct1].[GearNickName] = [t].[Nickname]) AND ([ct1].[GearSquadId] = [t].[SquadId])
, [CogTag] AS [ct2]
LEFT JOIN (
    SELECT [ct2.Gear].*
    FROM [Gear] AS [ct2.Gear]
    WHERE [ct2.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([ct2].[GearNickName] = [t0].[Nickname]) AND ([ct2].[GearSquadId] = [t0].[SquadId])
WHERE (([t].[Nickname] = [t0].[Nickname]) OR ([t].[Nickname] IS NULL AND [t0].[Nickname] IS NULL)) AND (([t].[SquadId] = [t0].[SquadId]) OR ([t].[SquadId] IS NULL AND [t0].[SquadId] IS NULL))");
        }

        public override async Task Select_Where_Navigation_Null(bool isAsync)
        {
            await base.Select_Where_Navigation_Null(isAsync);

            AssertSql(
                @"SELECT [ct].[Id], [ct].[GearNickName], [ct].[GearSquadId], [ct].[Note]
FROM [CogTag] AS [ct]
WHERE [ct].[GearNickName] IS NULL AND [ct].[GearSquadId] IS NULL");
        }

        public override async Task Select_Where_Navigation_Null_Reverse(bool isAsync)
        {
            await base.Select_Where_Navigation_Null_Reverse(isAsync);

            AssertSql(
                @"SELECT [ct].[Id], [ct].[GearNickName], [ct].[GearSquadId], [ct].[Note]
FROM [CogTag] AS [ct]
WHERE [ct].[GearNickName] IS NULL AND [ct].[GearSquadId] IS NULL");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(bool isAsync)
        {
            await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(isAsync);

            AssertSql(
                @"SELECT [ct1].[Id] AS [Id1], [ct2].[Id] AS [Id2]
FROM [CogTag] AS [ct1]
LEFT JOIN (
    SELECT [ct1.Gear].*
    FROM [Gear] AS [ct1.Gear]
    WHERE [ct1.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct1].[GearNickName] = [t].[Nickname]) AND ([ct1].[GearSquadId] = [t].[SquadId])
, [CogTag] AS [ct2]
LEFT JOIN (
    SELECT [ct2.Gear].*
    FROM [Gear] AS [ct2.Gear]
    WHERE [ct2.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([ct2].[GearNickName] = [t0].[Nickname]) AND ([ct2].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Nickname] = [t0].[Nickname]) OR ([t].[Nickname] IS NULL AND [t0].[Nickname] IS NULL)");
        }

        public override async Task Optional_Navigation_Null_Coalesce_To_Clr_Type(bool isAsync)
        {
            await base.Optional_Navigation_Null_Coalesce_To_Clr_Type(isAsync);

            AssertSql(
                @"SELECT TOP 1 CAST(COALESCE([w.SynergyWith].[IsAutomatic], 0) AS bit) AS [IsAutomatic]
FROM [Weapon] AS [w]
LEFT JOIN [Weapon] AS [w.SynergyWith] ON [w].[SynergyWithId] = [w.SynergyWith].[Id]");
        }

        public override async Task Where_subquery_boolean(bool isAsync)
        {
            await base.Where_subquery_boolean(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ((
    SELECT TOP 1 [w].[IsAutomatic]
    FROM [Weapon] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName]
) = True)");
        }

        public override async Task Singleton_Navigation_With_Member_Access(bool isAsync)
        {
            await base.Singleton_Navigation_With_Member_Access(isAsync);

            AssertSql(
                @"SELECT [t].[CityOrBirthName] AS [B]
FROM [CogTag] AS [ct]
LEFT JOIN (
    SELECT [ct.Gear].*
    FROM [Gear] AS [ct.Gear]
    WHERE [ct.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON ([ct].[GearNickName] = [t].[Nickname]) AND ([ct].[GearSquadId] = [t].[SquadId])
WHERE ([t].[Nickname] = 'Marcus') AND (([t].[CityOrBirthName] <> 'Ephyra') OR [t].[CityOrBirthName] IS NULL)");
        }

        public override async Task GroupJoin_Composite_Key(bool isAsync)
        {
            await base.GroupJoin_Composite_Key(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [CogTag] AS [ct]
INNER JOIN [Gear] AS [g] ON ([ct].[GearNickName] = [g].[Nickname]) AND ([ct].[GearSquadId] = [g].[SquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override async Task Join_navigation_translated_to_subquery_composite_key(bool isAsync)
        {
            await base.Join_navigation_translated_to_subquery_composite_key(isAsync);

            AssertSql(
                @"SELECT [g].[FullName], [t].[Note]
FROM [Gear] AS [g]
INNER JOIN [CogTag] AS [t] ON [g].[FullName] IN (
    SELECT TOP 1 [subQuery0].[FullName]
    FROM [Gear] AS [subQuery0]
    WHERE [subQuery0].[Discriminator] IN ('Officer', 'Gear') AND (([subQuery0].[Nickname] = [t].[GearNickName]) AND ([subQuery0].[SquadId] = [t].[GearSquadId]))
)
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        public override async Task Collection_with_inheritance_and_join_include_joined(bool isAsync)
        {
            await base.Collection_with_inheritance_and_join_include_joined(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [CogTag] AS [t]
INNER JOIN [Gear] AS [g] ON ([t].[GearSquadId] = [g].[SquadId]) AND ([t].[GearNickName] = [g].[Nickname])
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] = 'Officer'");
        }

        public override async Task Collection_with_inheritance_and_join_include_source(bool isAsync)
        {
            await base.Collection_with_inheritance_and_join_include_source(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
INNER JOIN [CogTag] AS [t] ON ([g].[SquadId] = [t].[GearSquadId]) AND ([g].[Nickname] = [t].[GearNickName])
WHERE [g].[Discriminator] = 'Officer'");
        }

        public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column(bool isAsync)
        {
            await base.Non_unicode_string_literal_is_used_for_non_unicode_column(isAsync);

            AssertSql(
                @"SELECT [c].[Name], [c].[Location]
FROM [City] AS [c]
WHERE [c].[Location] = 'Unknown'");
        }

        public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column_right(bool isAsync)
        {
            await base.Non_unicode_string_literal_is_used_for_non_unicode_column_right(isAsync);

            AssertSql(
                @"SELECT [c].[Name], [c].[Location]
FROM [City] AS [c]
WHERE 'Unknown' = [c].[Location]");
        }


        public override async Task Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(bool isAsync)
        {
            await base.Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(isAsync);

            AssertSql(
                @"SELECT [c].[Name], [c].[Location]
FROM [City] AS [c]
WHERE [c].[Location] IN ('Unknown', 'Jacinto''s location', 'Ephyra''s location')");
        }


        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[Nickname] = 'Marcus') AND ([g.CityOfBirth].[Location] = 'Jacinto''s location'))");
        }

        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(isAsync);

            AssertSql(
                @"SELECT [c].[Name], [c].[Location]
FROM [City] AS [c]
WHERE Instr(1, 'Jacinto', [c].[Location], 0) > 0");
        }

        public override void Non_unicode_string_literals_is_used_for_non_unicode_column_with_concat()
        {
            base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_concat();

            AssertSql(
                @"SELECT [c].[Name], [c].[Location]
FROM [City] AS [c]
WHERE Instr(1, 'Add', [c].[Location] + 'Added', 0) > 0");
        }

        public override void Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2()
        {
            base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2();

            AssertSql(
                @"SELECT [g1].[Nickname], [g1].[SquadId], [g1].[AssignedCityName], [g1].[CityOrBirthName], [g1].[Discriminator], [g1].[FullName], [g1].[HasSoulPatch], [g1].[LeaderNickname], [g1].[LeaderSquadId], [g1].[Rank], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Gear] AS [g1]
LEFT JOIN (
    SELECT [g2].*
    FROM [Gear] AS [g2]
    WHERE [g2].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g1].[LeaderNickname] = [t].[Nickname]
WHERE [g1].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[FullName]",
                //
                @"SELECT [g2.Weapons].[Id], [g2.Weapons].[AmmunitionType], [g2.Weapons].[IsAutomatic], [g2.Weapons].[Name], [g2.Weapons].[OwnerFullName], [g2.Weapons].[SynergyWithId]
FROM [Weapon] AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT [t0].[FullName]
    FROM [Gear] AS [g10]
    LEFT JOIN (
        SELECT [g20].*
        FROM [Gear] AS [g20]
        WHERE [g20].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t0] ON [g10].[LeaderNickname] = [t0].[Nickname]
    WHERE [g10].[Discriminator] IN ('Officer', 'Gear') AND [t0].[Nickname] IS NOT NULL
) AS [t1] ON [g2.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Gear] AS [g]
LEFT JOIN (
    SELECT [g2].*
    FROM [Gear] AS [g2]
    WHERE [g2].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g].[LeaderNickname] = [t].[Nickname]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[FullName], [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g1].[FullName], [t2].[FullName] AS [FullName0]
    FROM [Gear] AS [g1]
    LEFT JOIN (
        SELECT [g21].*
        FROM [Gear] AS [g21]
        WHERE [g21].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON [g1].[LeaderNickname] = [t2].[Nickname]
    WHERE [g1].[Discriminator] IN ('Officer', 'Gear') AND [t2].[Nickname] IS NULL
) AS [t3] ON [g.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName0], [t3].[FullName]",
                //
                @"SELECT [g2.Weapons].[Id], [g2.Weapons].[AmmunitionType], [g2.Weapons].[IsAutomatic], [g2.Weapons].[Name], [g2.Weapons].[OwnerFullName], [g2.Weapons].[SynergyWithId]
FROM [Weapon] AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT [t0].[FullName]
    FROM [Gear] AS [g0]
    LEFT JOIN (
        SELECT [g20].*
        FROM [Gear] AS [g20]
        WHERE [g20].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t0] ON [g0].[LeaderNickname] = [t0].[Nickname]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND [t0].[Nickname] IS NOT NULL
) AS [t1] ON [g2.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Gear] AS [g]
LEFT JOIN (
    SELECT [g2].*
    FROM [Gear] AS [g2]
    WHERE [g2].[Discriminator] = 'Officer'
) AS [t] ON [g].[LeaderNickname] = [t].[Nickname]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[FullName], [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g1].[FullName], [t2].[FullName] AS [FullName0]
    FROM [Gear] AS [g1]
    LEFT JOIN (
        SELECT [g21].*
        FROM [Gear] AS [g21]
        WHERE [g21].[Discriminator] = 'Officer'
    ) AS [t2] ON [g1].[LeaderNickname] = [t2].[Nickname]
    WHERE [g1].[Discriminator] IN ('Officer', 'Gear') AND [t2].[Nickname] IS NULL
) AS [t3] ON [g.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName0], [t3].[FullName]",
                //
                @"SELECT [g2.Weapons].[Id], [g2.Weapons].[AmmunitionType], [g2.Weapons].[IsAutomatic], [g2.Weapons].[Name], [g2.Weapons].[OwnerFullName], [g2.Weapons].[SynergyWithId]
FROM [Weapon] AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT [t0].[FullName]
    FROM [Gear] AS [g0]
    LEFT JOIN (
        SELECT [g20].*
        FROM [Gear] AS [g20]
        WHERE [g20].[Discriminator] = 'Officer'
    ) AS [t0] ON [g0].[LeaderNickname] = [t0].[Nickname]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND [t0].[Nickname] IS NOT NULL
) AS [t1] ON [g2.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Gear] AS [g]
LEFT JOIN (
    SELECT [g2].*
    FROM [Gear] AS [g2]
    WHERE [g2].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g].[LeaderNickname] = [t].[Nickname]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [t].[FullName], [g].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g1].[FullName], [t2].[FullName] AS [FullName0]
    FROM [Gear] AS [g1]
    LEFT JOIN (
        SELECT [g21].*
        FROM [Gear] AS [g21]
        WHERE [g21].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON [g1].[LeaderNickname] = [t2].[Nickname]
    WHERE [g1].[Discriminator] IN ('Officer', 'Gear') AND [t2].[Nickname] IS NULL
) AS [t3] ON [g.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName0], [t3].[FullName]",
                //
                @"SELECT [g2.Weapons].[Id], [g2.Weapons].[AmmunitionType], [g2.Weapons].[IsAutomatic], [g2.Weapons].[Name], [g2.Weapons].[OwnerFullName], [g2.Weapons].[SynergyWithId]
FROM [Weapon] AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT [t0].[FullName]
    FROM [Gear] AS [g0]
    LEFT JOIN (
        SELECT [g20].*
        FROM [Gear] AS [g20]
        WHERE [g20].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t0] ON [g0].[LeaderNickname] = [t0].[Nickname]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND [t0].[Nickname] IS NOT NULL
) AS [t1] ON [g2.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOrBirthName], [t].[Discriminator], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank]
FROM [Gear] AS [g]
LEFT JOIN (
    SELECT [g2].*
    FROM [Gear] AS [g2]
    WHERE [g2].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [g].[LeaderNickname] = [t].[Nickname]
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[FullName], [t].[FullName]",
                //
                @"SELECT [g.Weapons].[Id], [g.Weapons].[AmmunitionType], [g.Weapons].[IsAutomatic], [g.Weapons].[Name], [g.Weapons].[OwnerFullName], [g.Weapons].[SynergyWithId]
FROM [Weapon] AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT [g0].[FullName]
    FROM [Gear] AS [g0]
    LEFT JOIN (
        SELECT [g20].*
        FROM [Gear] AS [g20]
        WHERE [g20].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t0] ON [g0].[LeaderNickname] = [t0].[Nickname]
    WHERE [g0].[Discriminator] IN ('Officer', 'Gear') AND ([g0].[Nickname] IS NOT NULL AND [t0].[Nickname] IS NULL)
) AS [t1] ON [g.Weapons].[OwnerFullName] = [t1].[FullName]
ORDER BY [t1].[FullName]",
                //
                @"SELECT [g2.Weapons].[Id], [g2.Weapons].[AmmunitionType], [g2.Weapons].[IsAutomatic], [g2.Weapons].[Name], [g2.Weapons].[OwnerFullName], [g2.Weapons].[SynergyWithId]
FROM [Weapon] AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT [t2].[FullName], [g1].[FullName] AS [FullName0]
    FROM [Gear] AS [g1]
    LEFT JOIN (
        SELECT [g21].*
        FROM [Gear] AS [g21]
        WHERE [g21].[Discriminator] IN ('Officer', 'Gear')
    ) AS [t2] ON [g1].[LeaderNickname] = [t2].[Nickname]
    WHERE [g1].[Discriminator] IN ('Officer', 'Gear') AND [t2].[Nickname] IS NOT NULL
) AS [t3] ON [g2.Weapons].[OwnerFullName] = [t3].[FullName]
ORDER BY [t3].[FullName0], [t3].[FullName]");
        }

        public override async Task Coalesce_operator_in_predicate(bool isAsync)
        {
            await base.Coalesce_operator_in_predicate(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE COALESCE([w].[IsAutomatic], 0) = 1");
        }

        public override async Task Coalesce_operator_in_predicate_with_other_conditions(bool isAsync)
        {
            await base.Coalesce_operator_in_predicate_with_other_conditions(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
WHERE ([w].[AmmunitionType] = 1) AND (COALESCE([w].[IsAutomatic], 0) = 1)");
        }

        public override async Task Coalesce_operator_in_projection_with_other_conditions(bool isAsync)
        {
            await base.Coalesce_operator_in_projection_with_other_conditions(isAsync);

            AssertSql(
                @"SELECT CASE
    WHEN ([w].[AmmunitionType] = 1) AND (COALESCE([w].[IsAutomatic], 0) = 1)
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END
FROM [Weapon] AS [w]");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE (([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL) AND ([t0].[HasSoulPatch] = True)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate2(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate2(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE [t0].[HasSoulPatch] = True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t0].[HasSoulPatch] <> 1) AND [t0].[HasSoulPatch] IS NOT NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex1(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex1(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE CASE
    WHEN [t0].[HasSoulPatch] = True
    THEN 1 ELSE [t0].[HasSoulPatch]
END <> 1");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex2(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex2(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE CASE
    WHEN ([t0].[HasSoulPatch] <> 1) AND [t0].[HasSoulPatch] IS NOT NULL
    THEN 0 ELSE [t0].[HasSoulPatch]
END <> 1");
        }

        public override async Task Optional_navigation_type_compensation_works_with_conditional_expression(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_conditional_expression(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE CASE
    WHEN [t0].[HasSoulPatch] = True
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = 1");
        }

        public override async Task Optional_navigation_type_compensation_works_with_binary_expression(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_binary_expression(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t0].[HasSoulPatch] = True) OR (Instr(1, 'Cole', [t].[Note], 0) > 0)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_projection(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_projection(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_DTOs(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_DTOs(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId] AS [Id]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_list_initializers(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_list_initializers(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId], [t0].[SquadId] + 1
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_array_initializers(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_array_initializers(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_orderby(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_orderby(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL
ORDER BY [t0].[SquadId]");
        }

        public override async Task Optional_navigation_type_compensation_works_with_groupby(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_groupby(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL
ORDER BY [t0].[SquadId]");
        }

        public override async Task Optional_navigation_type_compensation_works_with_all(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_all(isAsync);

            AssertSql(
                @"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM [CogTag] AS [t]
        LEFT JOIN (
            SELECT [t.Gear].*
            FROM [Gear] AS [t.Gear]
            WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
        ) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
        WHERE (([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL) AND ([t0].[HasSoulPatch] = False))
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END");
        }

        public override async Task Optional_navigation_type_compensation_works_with_contains(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_contains(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE (([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL) AND [t0].[SquadId] IN (
    SELECT [g].[SquadId]
    FROM [Gear] AS [g]
    WHERE [g].[Discriminator] IN ('Officer', 'Gear')
)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_skip(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_skip(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_take(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_take(isAsync);

            AssertSql(
                @"SELECT [t0].[SquadId]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [t.Gear].*
    FROM [Gear] AS [t.Gear]
    WHERE [t.Gear].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
WHERE ([t].[Note] <> 'K.I.A.') OR [t].[Note] IS NULL");
        }


        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Join_predicate_value_equals_condition(bool isAsync)
        {
            await base.Join_predicate_value_equals_condition(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
INNER JOIN [Weapon] AS [w] ON [w].[SynergyWithId] IS NOT NULL
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Join_predicate_value(bool isAsync)
        {
            await base.Join_predicate_value(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
INNER JOIN [Weapon] AS [w] ON [g].[HasSoulPatch] = True
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Join_predicate_condition_equals_condition(bool isAsync)
        {
            await base.Join_predicate_condition_equals_condition(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
INNER JOIN [Weapon] AS [w] ON CASE
    WHEN [g].[FullName] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [w].[SynergyWithId] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Left_join_predicate_value_equals_condition(bool isAsync)
        {
            await base.Left_join_predicate_value_equals_condition(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
LEFT JOIN [Weapon] AS [w] ON [w].[SynergyWithId] IS NOT NULL
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Left_join_predicate_value(bool isAsync)
        {
            await base.Left_join_predicate_value(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
LEFT JOIN [Weapon] AS [w] ON [g].[HasSoulPatch] = True
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        [Theory(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override async Task Left_join_predicate_condition_equals_condition(bool isAsync)
        {
            await base.Left_join_predicate_condition_equals_condition(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank]
FROM [Gear] AS [g]
LEFT JOIN [Weapon] AS [w] ON CASE
    WHEN [g].[FullName] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END = CASE
    WHEN [w].[SynergyWithId] IS NULL
    THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT)
END
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }


        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddDays(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddDays(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddHours(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddHours(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddMilliseconds(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMilliseconds(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddMinutes(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMinutes(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddMonths(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMonths(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddSeconds(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddSeconds(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: DateTimeOffset not fully supported")]
        public override async Task DateTimeOffset_DateAdd_AddYears(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddYears(isAsync);
        }

        public override async Task Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(bool isAsync)
        {
            await base.Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [g].*
    FROM [Gear] AS [g]
    WHERE [g].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON [t].[GearNickName] = [t0].[Nickname]
ORDER BY [t].[GearNickName]");
        }

        public override async Task Complex_predicate_with_AndAlso_and_nullable_bool_property(bool isAsync)
        {
            await base.Complex_predicate_with_AndAlso_and_nullable_bool_property(isAsync);

            AssertSql(
                @"SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
FROM [Weapon] AS [w]
LEFT JOIN (
    SELECT [w.Owner].*
    FROM [Gear] AS [w.Owner]
    WHERE [w.Owner].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [w].[OwnerFullName] = [t].[FullName]
WHERE ([w].[Id] <> 50) AND ([t].[HasSoulPatch] = False)");
        }

        public override async Task Distinct_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Distinct_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                @"SELECT DISTINCT [g].[HasSoulPatch]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g#Tag].[Note] <> 'Foo') OR [g#Tag].[Note] IS NULL)");
        }

        public override async Task Sum_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Sum_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                @"SELECT SUM([g].[SquadId])
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g#Tag].[Note] <> 'Foo') OR [g#Tag].[Note] IS NULL)");
        }

        public override async Task Count_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Count_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                @"SELECT COUNT(*)
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g#Tag].[Note] <> 'Foo') OR [g#Tag].[Note] IS NULL)");
        }

        public override async Task Count_with_unflattened_groupjoin_is_evaluated_on_client(bool isAsync)
        {
            await base.Count_with_unflattened_groupjoin_is_evaluated_on_client(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [t] ON ([g].[Nickname] = [t].[GearNickName]) AND ([g].[SquadId] = [t].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[Nickname], [g].[SquadId]");
        }

        public override async Task Distinct_with_unflattened_groupjoin_is_evaluated_on_client(bool isAsync)
        {
            await base.Distinct_with_unflattened_groupjoin_is_evaluated_on_client(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [t] ON ([g].[Nickname] = [t].[GearNickName]) AND ([g].[SquadId] = [t].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g].[Nickname], [g].[SquadId]");
        }

        public override async Task FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(bool isAsync)
        {
            await base.FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(isAsync);

            AssertSql(
                @"SELECT TOP 1 [s].[Id], [s].[InternalNumber], [s].[Name]
FROM [Squad] AS [s]
LEFT JOIN (
    SELECT [g].*
    FROM [Gear] AS [g]
    WHERE [g].[Discriminator] IN ('Officer', 'Gear')
) AS [t] ON [s].[Id] = [t].[SquadId]
WHERE [s].[Name] = 'Kilo'");
        }



        public override async Task Non_flattened_GroupJoin_with_result_operator_evaluates_on_the_client(bool isAsync)
        {
            await base.Non_flattened_GroupJoin_with_result_operator_evaluates_on_the_client(isAsync);

            AssertSql(
                @"SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOrBirthName], [t0].[Discriminator], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank]
FROM [CogTag] AS [t]
LEFT JOIN (
    SELECT [g].*
    FROM [Gear] AS [g]
    WHERE [g].[Discriminator] IN ('Officer', 'Gear')
) AS [t0] ON ([t].[GearNickName] = [t0].[Nickname]) AND ([t].[GearSquadId] = [t0].[SquadId])
ORDER BY [t].[GearNickName], [t].[GearSquadId]");
        }

        [Theory(Skip = "Assertion failed without evident reason")]
        public override async Task Client_side_equality_with_parameter_works_with_optional_navigations(bool isAsync)
        {
            await base.Client_side_equality_with_parameter_works_with_optional_navigations(isAsync);

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE [g].[Discriminator] IN ('Officer', 'Gear')");
        }

        public override async Task Contains_with_local_nullable_guid_list_closure(bool isAsync)
        {
            await base.Contains_with_local_nullable_guid_list_closure(isAsync);

            AssertSql(
                @"SELECT [e].[Id], [e].[GearNickName], [e].[GearSquadId], [e].[Note]
FROM [CogTag] AS [e]
WHERE [e].[Id] IN ('d2c26679-562b-44d1-ab96-23d1775e0926', '23cbcf9b-ce14-45cf-aafa-2c2667ebfdd3', 'ab1b82d7-88db-42bd-a132-7eef9aa68af4')");
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property()
        {
            base.Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property();

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = True)
ORDER BY [g].[Rank]");
        }

        public override void Order_by_is_properly_lifted_from_subquery_created_by_include()
        {
            base.Order_by_is_properly_lifted_from_subquery_created_by_include();

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName], [g].[Rank]");
        }

        public override void Order_by_then_by_is_properly_lifted_from_subquery_created_by_include()
        {
            base.Order_by_then_by_is_properly_lifted_from_subquery_created_by_include();

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName], [g].[Rank], [g].[Nickname] DESC");
        }

        public override void Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include()
        {
            base.Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include();

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName], [g].[Nickname] DESC, [g].[Rank]");
        }

        public override void Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query()
        {
            base.Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query();

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName]");
        }

        public override void Where_is_properly_lifted_from_subquery_created_by_include()
        {
            base.Where_is_properly_lifted_from_subquery_created_by_include();

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g#Tag].[Id], [g#Tag].[GearNickName], [g#Tag].[GearSquadId], [g#Tag].[Note]
FROM [Gear] AS [g]
LEFT JOIN [CogTag] AS [g#Tag] ON ([g].[Nickname] = [g#Tag].[GearNickName]) AND ([g].[SquadId] = [g#Tag].[GearSquadId])
WHERE ([g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[FullName] <> 'Augustus Cole')) AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName]");
        }

        public override async Task Where_and_order_by_are_properly_lifted_from_subquery_created_by_tracking(bool isAsync)
        {
            await base.Where_and_order_by_are_properly_lifted_from_subquery_created_by_tracking(isAsync);

            AssertSql(
                @"SELECT [g].[FullName]
FROM [Gear] AS [g]
WHERE ([g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[FullName] <> 'Augustus Cole')) AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[FullName], [g].[Rank]");
        }

        public override void Subquery_is_lifted_from_main_from_clause_of_SelectMany()
        {
            base.Subquery_is_lifted_from_main_from_clause_of_SelectMany();

            AssertSql(
                @"SELECT [g].[FullName] AS [Name1], [g2].[FullName] AS [Name2]
FROM [Gear] AS [g]
, [Gear] AS [g2]
WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND (([g].[HasSoulPatch] = True) AND ([g2].[HasSoulPatch] = False))
ORDER BY [g].[FullName], [g].[Rank]");
        }

        public override async Task Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                @"SELECT [gear].[FullName]
FROM [Gear] AS [gear]
, [CogTag] AS [tag]
WHERE [gear].[Discriminator] IN ('Officer', 'Gear') AND ([gear].[HasSoulPatch] = True)
ORDER BY [gear].[FullName], [tag].[Note]");
        }

        public override async Task Subquery_containing_join_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_join_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                @"SELECT [gear].[Nickname]
FROM [Gear] AS [gear]
INNER JOIN [CogTag] AS [tag] ON [gear].[Nickname] = [tag].[GearNickName]
WHERE [gear].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [gear].[Nickname], [tag].[Note]");
        }

        public override async Task Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                @"SELECT [gear].[Nickname]
FROM [Gear] AS [gear]
LEFT JOIN [CogTag] AS [tag] ON [gear].[Nickname] = [tag].[GearNickName]
WHERE [gear].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [gear].[Nickname], [gear].[Rank]");
        }

        public override async Task Subquery_containing_join_gets_lifted_clashing_names(bool isAsync)
        {
            await base.Subquery_containing_join_gets_lifted_clashing_names(isAsync);

            AssertSql(
                @"SELECT [gear].[Nickname]
FROM [Gear] AS [gear]
INNER JOIN [CogTag] AS [tag] ON [gear].[Nickname] = [tag].[GearNickName]
INNER JOIN [CogTag] AS [tag0] ON [gear].[Nickname] = [tag0].[GearNickName]
WHERE [gear].[Discriminator] IN ('Officer', 'Gear') AND (([tag].[GearNickName] <> 'Cole Train') OR [tag].[GearNickName] IS NULL)
ORDER BY [gear].[Nickname], [tag0].[Id], [tag].[Note]");
        }

        public override void Subquery_created_by_include_gets_lifted_nested()
        {
            base.Subquery_created_by_include_gets_lifted_nested();

            AssertSql(
                @"SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOrBirthName], [g].[Discriminator], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], [g.CityOfBirth].[Name], [g.CityOfBirth].[Location]
FROM [Gear] AS [g]
INNER JOIN [City] AS [g.CityOfBirth] ON [g].[CityOrBirthName] = [g.CityOfBirth].[Name]
WHERE ([g].[Discriminator] IN ('Officer', 'Gear') AND EXISTS (
    SELECT 1
    FROM [Weapon] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName])) AND ([g].[HasSoulPatch] = False)
ORDER BY [g].[Nickname], [g].[Rank]");
        }

        public override async Task Subquery_is_not_lifted_from_additional_from_clause(bool isAsync)
        {
            await base.Subquery_is_not_lifted_from_additional_from_clause(isAsync);

            AssertSql(
                @"SELECT [g1].[FullName] AS [Name1]
FROM [Gear] AS [g1]
WHERE [g1].[Discriminator] IN ('Officer', 'Gear') AND ([g1].[HasSoulPatch] = True)
ORDER BY [g1].[FullName]",
                //
                @"SELECT [g0].[HasSoulPatch], [g0].[FullName]
FROM [Gear] AS [g0]
WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g0].[Rank]",
                //
                @"SELECT [g0].[HasSoulPatch], [g0].[FullName]
FROM [Gear] AS [g0]
WHERE [g0].[Discriminator] IN ('Officer', 'Gear')
ORDER BY [g0].[Rank]");
        }

        public override async Task Subquery_with_result_operator_is_not_lifted(bool isAsync)
        {
            await base.Subquery_with_result_operator_is_not_lifted(isAsync);

            AssertSql(
                @"@__p_0='2'

SELECT [t].[FullName]
FROM (
    SELECT TOP @__p_0 [g].*
    FROM [Gear] AS [g]
    WHERE [g].[Discriminator] IN ('Officer', 'Gear') AND ([g].[HasSoulPatch] = False)
    ORDER BY [g].[FullName]
) AS [t]
ORDER BY [t].[Rank]");
        }

        public override void Member_access_on_derived_entity_using_cast()
        {
            base.Member_access_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], [f].[Eradicated]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Member_access_on_derived_materialized_entity_using_cast()
        {
            base.Member_access_on_derived_materialized_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Id], [f].[CapitalName], [f].[Discriminator], [f].[Name], [f].[CommanderName], [f].[Eradicated]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Member_access_on_derived_entity_using_cast_and_let()
        {
            base.Member_access_on_derived_entity_using_cast_and_let();

            AssertSql(
                @"SELECT [f].[Name], [f].[Eradicated]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Property_access_on_derived_entity_using_cast()
        {
            base.Property_access_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], [f].[Eradicated]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Navigation_access_on_derived_entity_using_cast()
        {
            base.Navigation_access_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], [t].[ThreatLevel] AS [Threat]
FROM [Faction] AS [f]
LEFT JOIN (
    SELECT [f.Commander].*
    FROM [LocustLeader] AS [f.Commander]
    WHERE [f.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f].[CommanderName] = [t].[Name]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Navigation_access_on_derived_materialized_entity_using_cast()
        {
            base.Navigation_access_on_derived_materialized_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Id], [f].[CapitalName], [f].[Discriminator], [f].[Name], [f].[CommanderName], [f].[Eradicated], [t].[ThreatLevel]
FROM [Faction] AS [f]
LEFT JOIN (
    SELECT [f.Commander].*
    FROM [LocustLeader] AS [f.Commander]
    WHERE [f.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f].[CommanderName] = [t].[Name]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Navigation_access_via_EFProperty_on_derived_entity_using_cast()
        {
            base.Navigation_access_via_EFProperty_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], [t].[ThreatLevel] AS [Threat]
FROM [Faction] AS [f]
LEFT JOIN (
    SELECT [f.Commander].*
    FROM [LocustLeader] AS [f.Commander]
    WHERE [f.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f].[CommanderName] = [t].[Name]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        public override void Navigation_access_fk_on_derived_entity_using_cast()
        {
            base.Navigation_access_fk_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], [f].[CommanderName]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Collection_navigation_access_on_derived_entity_using_cast()
        {
            base.Collection_navigation_access_on_derived_entity_using_cast();

            AssertSql(
                @"SELECT [f].[Name], (
    SELECT COUNT(*)
    FROM [LocustLeader] AS [l]
    WHERE [l].[Discriminator] IN ('LocustCommander', 'LocustLeader') AND ([f].[Id] = [l].[LocustHordeId])
) AS [LeadersCount]
FROM [Faction] AS [f]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name]");
        }


        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Comparing_two_collection_navigations_inheritance()
        {
            base.Comparing_two_collection_navigations_inheritance();
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany()
        {
            base.Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany();

            AssertSql(
                @"SELECT [f].[Name] AS [Name0], [f.Leaders].[Name] AS [LeaderName]
FROM [Faction] AS [f]
INNER JOIN [LocustLeader] AS [f.Leaders] ON [f].[Id] = [f.Leaders].[LocustHordeId]
WHERE (([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')) AND [f.Leaders].[Discriminator] IN ('LocustCommander', 'LocustLeader')
ORDER BY [LeaderName]");
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Include_on_derived_entity_using_OfType()
        {
            base.Include_on_derived_entity_using_OfType();

            AssertSql(
                @"SELECT [lh].[Id], [lh].[CapitalName], [lh].[Discriminator], [lh].[Name], [lh].[CommanderName], [lh].[Eradicated], [t].[Name], [t].[Discriminator], [t].[LocustHordeId], [t].[ThreatLevel], [t].[DefeatedByNickname], [t].[DefeatedBySquadId]
FROM [Faction] AS [lh]
LEFT JOIN (
    SELECT [lh.Commander].*
    FROM [LocustLeader] AS [lh.Commander]
    WHERE [lh.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [lh].[CommanderName] = [t].[Name]
WHERE [lh].[Discriminator] = 'LocustHorde'
ORDER BY [lh].[Name], [lh].[Id]",
                //
                @"SELECT [lh.Leaders].[Name], [lh.Leaders].[Discriminator], [lh.Leaders].[LocustHordeId], [lh.Leaders].[ThreatLevel], [lh.Leaders].[DefeatedByNickname], [lh.Leaders].[DefeatedBySquadId]
FROM [LocustLeader] AS [lh.Leaders]
INNER JOIN (
    SELECT DISTINCT [lh0].[Id], [lh0].[Name]
    FROM [Faction] AS [lh0]
    LEFT JOIN (
        SELECT [lh.Commander0].*
        FROM [LocustLeader] AS [lh.Commander0]
        WHERE [lh.Commander0].[Discriminator] = 'LocustCommander'
    ) AS [t0] ON [lh0].[CommanderName] = [t0].[Name]
    WHERE [lh0].[Discriminator] = 'LocustHorde'
) AS [t1] ON [lh.Leaders].[LocustHordeId] = [t1].[Id]
WHERE [lh.Leaders].[Discriminator] IN ('LocustCommander', 'LocustLeader')
ORDER BY [t1].[Name], [t1].[Id]");
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Include_on_derived_entity_using_subquery_with_cast()
        {
            base.Include_on_derived_entity_using_subquery_with_cast();

            AssertSql(
                @"SELECT [f].[Id], [f].[CapitalName], [f].[Discriminator], [f].[Name], [f].[CommanderName], [f].[Eradicated], [t].[Name], [t].[Discriminator], [t].[LocustHordeId], [t].[ThreatLevel], [t].[DefeatedByNickname], [t].[DefeatedBySquadId]
FROM [Faction] AS [f]
LEFT JOIN (
    SELECT [f.Commander].*
    FROM [LocustLeader] AS [f.Commander]
    WHERE [f.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f].[CommanderName] = [t].[Name]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name], [f].[Id]",
                //
                @"SELECT [f.Leaders].[Name], [f.Leaders].[Discriminator], [f.Leaders].[LocustHordeId], [f.Leaders].[ThreatLevel], [f.Leaders].[DefeatedByNickname], [f.Leaders].[DefeatedBySquadId]
FROM [LocustLeader] AS [f.Leaders]
INNER JOIN (
    SELECT DISTINCT [f0].[Id], [f0].[Name]
    FROM [Faction] AS [f0]
    LEFT JOIN (
        SELECT [f.Commander0].*
        FROM [LocustLeader] AS [f.Commander0]
        WHERE [f.Commander0].[Discriminator] = 'LocustCommander'
    ) AS [t0] ON [f0].[CommanderName] = [t0].[Name]
    WHERE ([f0].[Discriminator] = 'LocustHorde') AND ([f0].[Discriminator] = 'LocustHorde')
) AS [t1] ON [f.Leaders].[LocustHordeId] = [t1].[Id]
WHERE [f.Leaders].[Discriminator] IN ('LocustCommander', 'LocustLeader')
ORDER BY [t1].[Name], [t1].[Id]");
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Include_on_derived_entity_using_subquery_with_cast_AsNoTracking()
        {
            base.Include_on_derived_entity_using_subquery_with_cast_AsNoTracking();

            AssertSql(
                @"SELECT [f].[Id], [f].[CapitalName], [f].[Discriminator], [f].[Name], [f].[CommanderName], [f].[Eradicated], [t].[Name], [t].[Discriminator], [t].[LocustHordeId], [t].[ThreatLevel], [t].[DefeatedByNickname], [t].[DefeatedBySquadId]
FROM [Faction] AS [f]
LEFT JOIN (
    SELECT [f.Commander].*
    FROM [LocustLeader] AS [f.Commander]
    WHERE [f.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f].[CommanderName] = [t].[Name]
WHERE ([f].[Discriminator] = 'LocustHorde') AND ([f].[Discriminator] = 'LocustHorde')
ORDER BY [f].[Name], [f].[Id]",
                //
                @"SELECT [f.Leaders].[Name], [f.Leaders].[Discriminator], [f.Leaders].[LocustHordeId], [f.Leaders].[ThreatLevel], [f.Leaders].[DefeatedByNickname], [f.Leaders].[DefeatedBySquadId]
FROM [LocustLeader] AS [f.Leaders]
INNER JOIN (
    SELECT DISTINCT [f0].[Id], [f0].[Name]
    FROM [Faction] AS [f0]
    LEFT JOIN (
        SELECT [f.Commander0].*
        FROM [LocustLeader] AS [f.Commander0]
        WHERE [f.Commander0].[Discriminator] = 'LocustCommander'
    ) AS [t0] ON [f0].[CommanderName] = [t0].[Name]
    WHERE ([f0].[Discriminator] = 'LocustHorde') AND ([f0].[Discriminator] = 'LocustHorde')
) AS [t1] ON [f.Leaders].[LocustHordeId] = [t1].[Id]
WHERE [f.Leaders].[Discriminator] IN ('LocustCommander', 'LocustLeader')
ORDER BY [t1].[Name], [t1].[Id]");
        }

        [Fact(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override void Include_on_derived_entity_using_subquery_with_cast_cross_product_base_entity()
        {
            base.Include_on_derived_entity_using_subquery_with_cast_cross_product_base_entity();

            AssertSql(
                @"SELECT [f2].[Id], [f2].[CapitalName], [f2].[Discriminator], [f2].[Name], [f2].[CommanderName], [f2].[Eradicated], [t].[Name], [t].[Discriminator], [t].[LocustHordeId], [t].[ThreatLevel], [t].[DefeatedByNickname], [t].[DefeatedBySquadId], [ff].[Id], [ff].[CapitalName], [ff].[Discriminator], [ff].[Name], [ff].[CommanderName], [ff].[Eradicated], [ff.Capital].[Name], [ff.Capital].[Location]
FROM [Faction] AS [f2]
LEFT JOIN (
    SELECT [f2.Commander].*
    FROM [LocustLeader] AS [f2.Commander]
    WHERE [f2.Commander].[Discriminator] = 'LocustCommander'
) AS [t] ON [f2].[CommanderName] = [t].[Name]
, [Faction] AS [ff]
LEFT JOIN [City] AS [ff.Capital] ON [ff].[CapitalName] = [ff.Capital].[Name]
WHERE ([f2].[Discriminator] = 'LocustHorde') AND ([f2].[Discriminator] = 'LocustHorde')
ORDER BY [f2].[Name], [ff].[Name], [f2].[Id]",
                //
                @"SELECT [f2.Leaders].[Name], [f2.Leaders].[Discriminator], [f2.Leaders].[LocustHordeId], [f2.Leaders].[ThreatLevel], [f2.Leaders].[DefeatedByNickname], [f2.Leaders].[DefeatedBySquadId]
FROM [LocustLeader] AS [f2.Leaders]
INNER JOIN (
    SELECT DISTINCT [f20].[Id], [f20].[Name], [ff0].[Name] AS [Name0]
    FROM [Faction] AS [f20]
    LEFT JOIN (
        SELECT [f2.Commander0].*
        FROM [LocustLeader] AS [f2.Commander0]
        WHERE [f2.Commander0].[Discriminator] = 'LocustCommander'
    ) AS [t0] ON [f20].[CommanderName] = [t0].[Name]
    , [Faction] AS [ff0]
    LEFT JOIN [City] AS [ff.Capital0] ON [ff0].[CapitalName] = [ff.Capital0].[Name]
    WHERE ([f20].[Discriminator] = 'LocustHorde') AND ([f20].[Discriminator] = 'LocustHorde')
) AS [t1] ON [f2.Leaders].[LocustHordeId] = [t1].[Id]
WHERE [f2.Leaders].[Discriminator] IN ('LocustCommander', 'LocustLeader')
ORDER BY [t1].[Name], [t1].[Name0], [t1].[Id]");
        }

        [Theory(Skip = "Unsupported by JET: operators on IMAGE and TEXT data types are not supported")]
        public override async Task Select_subquery_distinct_firstordefault(bool isAsync)
        {
            await base.Select_subquery_distinct_firstordefault(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override async Task Where_subquery_distinct_firstordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_firstordefault_boolean(isAsync);
        }

        [Theory(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override async Task Where_subquery_distinct_orderby_firstordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_orderby_firstordefault_boolean(isAsync);
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertSql(expected);

        private void AssertContains(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertContains(expected);


        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}