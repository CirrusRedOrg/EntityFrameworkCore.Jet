using System;
using System.Collections.Generic;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.IntegrationTests.Model41.Demo.Data.EntityModels;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model41
{
    namespace Demo.Data.EntityModels
    {
        public class Applicant
        {
            public Guid Id { get; set; }

            public string E_FirstName { get; set; }

            public string E_LastName { get; set; }

            public DateTime DateOfBirth { get; set; }

            public virtual IList<Applicant_Address> Addresses { get; set; }
        }
    }

    namespace Demo.Data.EntityModels
    {
        public class Applicant_Address
        {
            public Guid Id { get; set; }

            public Guid Applicant_Id { get; set; }

            public virtual Applicant Applicant { get; set; }

            public string E_Flat_Unit { get; set; }

            public string E_Building { get; set; }

            public string E_Street { get; set; }

            public string E_Locality { get; set; }

            public string E_Town { get; set; }

            public string E_County { get; set; }

            public string E_PostCode { get; set; }
        }
    }

    public class DemoContext : DbContext
    {

        public DemoContext(DbContextOptions options) : base(options)
        {
        }


        public DbSet<Applicant> Applicants { get; set; }

        public DbSet<Applicant_Address> Applicant_Addresses { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // https://stackoverflow.com/questions/37493095/entity-framework-core-rc2-table-name-pluralization
            // No pluralization service implemented in EF Core
            //modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();

            modelBuilder.Entity<Applicant>().HasKey(k => k.Id);
            modelBuilder.Entity<Applicant_Address>().HasKey(k => k.Id);

            modelBuilder.Entity<Applicant_Address>()
                .HasOne(_ => _.Applicant)
                .WithMany(_ => _.Addresses)
                .IsRequired()
                .HasForeignKey(a => a.Applicant_Id);

        }
    }
}
