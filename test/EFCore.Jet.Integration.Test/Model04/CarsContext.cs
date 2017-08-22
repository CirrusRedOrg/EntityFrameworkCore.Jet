using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model04
{
    public class CarsContext : DbContext
    {
        public CarsContext(DbContextOptions options) : base (options)
        {}

        public DbSet<Car> Cars { get; set; }
        public DbSet<Person> Persons { get; set; }
    }
}
