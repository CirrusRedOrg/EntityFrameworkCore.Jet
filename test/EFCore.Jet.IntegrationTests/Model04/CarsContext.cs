using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model04
{
    public class CarsContext : DbContext
    {
        public CarsContext(DbContextOptions options) : base (options)
        {}

        public DbSet<Car> Cars { get; set; }
        public DbSet<Person> Persons { get; set; }
    }
}
