using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model38_OneEntity2Tables
{
    class Context : DbContext
    {
        public Context(DbConnection connection) : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        {
            
        }

        public DbSet<Student> Students { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(delegate(EntityMappingConfiguration<Student> studentConfig)
            {
                studentConfig.Properties(p => new { p.Id, p.StudentName });
                studentConfig.ToTable("StudentInfo");
            });

            Action<EntityMappingConfiguration<Student>> studentMapping = m =>
            {
                m.Properties(p => new { p.Id, p.Height, p.Weight, p.Photo, p.DateOfBirth });
                m.ToTable("StudentInfoDetail");
            };
            modelBuilder.Entity<Student>().Map(studentMapping);

        }
    }
}
