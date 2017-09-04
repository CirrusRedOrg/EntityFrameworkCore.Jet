using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace E2ETest.Namespace
{
    public partial class NonNullBoolWithDefaultContext : DbContext
    {
        public virtual DbSet<NonNullBoolWithDefault> NonNullBoolWithDefault { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. See http://go.microsoft.com/fwlink/?LinkId=723263 for guidance on storing connection strings.
                optionsBuilder.UseJet(@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=NonNullBooleanWithDefaultConstraint.accdb");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NonNullBoolWithDefault>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();

                entity.Property(e => e.BoolWithDefaultValueSql)
                    .HasColumnType("bit")
                    .HasDefaultValueSql("True");

                entity.Property(e => e.BoolWithoutDefaultValueSql).HasColumnType("bit");
            });
        }
    }
}
