// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace EntityFrameworkCore.Jet.IntegrationTests.GearOfWar
{
    public class GearsOfWarContext : DbContext
    {
        public static readonly string StoreName = "GearsOfWar";

        public GearsOfWarContext(DbContextOptions<GearsOfWarContext> options)
            : base(options)
        {
        }

        public DbSet<Gear> Gears { get; set; }
        public DbSet<Squad> Squads { get; set; }
        public DbSet<CogTag> Tags { get; set; }
        public DbSet<Weapon> Weapons { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Mission> Missions { get; set; }
        public DbSet<SquadMission> SquadMissions { get; set; }
        public DbSet<Faction> Factions { get; set; }
        public DbSet<LocustLeader> LocustLeaders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<City>()
                .HasKey(c => c.Name);

            modelBuilder.Entity<Gear>(
                b =>
                {
                    b.HasKey(g => new {g.Nickname, g.SquadId});

                    b.HasOne(g => g.CityOfBirth)
                        .WithMany(c => c.BornGears)
                        .HasForeignKey(g => g.CityOrBirthName)
                        .IsRequired();
                    b.HasOne(g => g.Tag)
                        .WithOne(t => t.Gear)
                        .HasForeignKey<CogTag>(t => new {t.GearNickName, t.GearSquadId});
                    b.HasOne(g => g.AssignedCity)
                        .WithMany(c => c.StationedGears)
                        .IsRequired(false);
                });

            modelBuilder.Entity<Officer>()
                .HasMany(o => o.Reports)
                .WithOne()
                .HasForeignKey(o => new {o.LeaderNickname, o.LeaderSquadId});

            modelBuilder.Entity<Squad>(
                b =>
                {
                    b.HasKey(s => s.Id);
                    b.Property(s => s.Id)
                        .ValueGeneratedNever();
                    b.HasMany(s => s.Members)
                        .WithOne(g => g.Squad)
                        .HasForeignKey(g => g.SquadId);
                });

            modelBuilder.Entity<Weapon>(
                b =>
                {
                    b.Property(w => w.Id)
                        .ValueGeneratedNever();
                    b.HasOne(w => w.SynergyWith)
                        .WithOne()
                        .HasForeignKey<Weapon>(w => w.SynergyWithId);
                    b.HasOne(w => w.Owner)
                        .WithMany(g => g.Weapons)
                        .HasForeignKey(w => w.OwnerFullName)
                        .HasPrincipalKey(g => g.FullName);
                });

            modelBuilder.Entity<Mission>()
                .Property(m => m.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<SquadMission>(
                b =>
                {
                    b.HasKey(sm => new {sm.SquadId, sm.MissionId});
                    b.HasOne(sm => sm.Mission)
                        .WithMany(m => m.ParticipatingSquads)
                        .HasForeignKey(sm => sm.MissionId);
                    b.HasOne(sm => sm.Squad)
                        .WithMany(s => s.Missions)
                        .HasForeignKey(sm => sm.SquadId);
                });

            modelBuilder.Entity<Faction>()
                .HasKey(f => f.Id);
            modelBuilder.Entity<Faction>()
                .Property(f => f.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<LocustHorde>()
                .HasBaseType<Faction>();
            modelBuilder.Entity<LocustHorde>()
                .HasMany(h => h.Leaders)
                .WithOne();
            modelBuilder.Entity<LocustHorde>()
                .HasOne(h => h.Commander)
                .WithOne(c => c.CommandingFaction);

            modelBuilder.Entity<LocustLeader>()
                .HasKey(l => l.Name);
            modelBuilder.Entity<LocustCommander>()
                .HasBaseType<LocustLeader>();
            modelBuilder.Entity<LocustCommander>()
                .HasOne(c => c.DefeatedBy)
                .WithOne()
                .HasForeignKey<LocustCommander>(c => new {c.DefeatedByNickname, c.DefeatedBySquadId});

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(entity.DisplayName());
            }
        }

        public static void Seed(GearsOfWarContext context)
        {
            var squads = GearsOfWarData.CreateSquads();
            var missions = GearsOfWarData.CreateMissions();
            var squadMissions = GearsOfWarData.CreateSquadMissions();
            var cities = GearsOfWarData.CreateCities();
            var weapons = GearsOfWarData.CreateWeapons();
            var tags = GearsOfWarData.CreateTags();
            var gears = GearsOfWarData.CreateGears();
            var locustLeaders = GearsOfWarData.CreateLocustLeaders();
            var factions = GearsOfWarData.CreateFactions();

            GearsOfWarData.WireUp(squads, missions, squadMissions, cities, weapons, tags, gears, locustLeaders, factions);

            context.Squads.AddRange(squads);
            context.Missions.AddRange(missions);
            context.SquadMissions.AddRange(squadMissions);
            context.Cities.AddRange(cities);
            context.Weapons.AddRange(weapons);
            context.Tags.AddRange(tags);
            context.Gears.AddRange(gears);
            context.LocustLeaders.AddRange(locustLeaders);
            context.Factions.AddRange(factions);
            context.SaveChanges();

            GearsOfWarData.WireUp2(locustLeaders, factions);

            context.SaveChanges();
        }
    }
}