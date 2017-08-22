using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model47_200
{
    class MyContext : DbContext
    {
        public string Schema { get; private set; }

        public MyContext(string schema) : base()
        {
            
        }

        // Your DbSets here
        DbSet<Emp> Emps { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Emp>()
                .ToTable("Emps", Schema);
        }
    }
}
