using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model40_HardMapping
{
    class Context : DbContext
    {
        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        public DbSet<Dog> Dogs { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Car> Cars { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasMany<Dog>(user => user.OwnedDogs)
                .WithMany(dog => dog.Owners)
                .Map(mapping =>
                {
                    mapping.MapLeftKey("OwnerId");
                    mapping.MapRightKey("DogId");
                    mapping.ToTable("Owner_Dog");
                });

            modelBuilder.Entity<User>()
                .HasMany<Car>(user => user.OwnedCars)
                .WithMany(car => car.Owners)
                .Map(mapping =>
                {
                    mapping.MapLeftKey("OwnerId");
                    mapping.MapRightKey("CarId");
                    mapping.ToTable("Owner_Car");
                });

            modelBuilder.Entity<Car>()
            .HasOptional(car => car.Owner)
            .WithOptionalDependent()
            .Map(_ => _.MapKey("OwnerId"));

            modelBuilder.Entity<User>()
                .HasMany(u => u.OwnedCars)
                .WithRequired(c => c.Owner)
                .HasForeignKey(_ => _.OwnerId);

        }
    }
}
