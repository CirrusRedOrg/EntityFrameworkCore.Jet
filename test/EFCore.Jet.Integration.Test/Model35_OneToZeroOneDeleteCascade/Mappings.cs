using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model35_OneToZeroOneDeleteCascade
{
    public class PrincipalMap : IEntityTypeConfiguration<Principal>
    {
        public void Configure(EntityTypeBuilder<Principal> builder)
        {
            builder.ToTable("PRINCIPALS");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("PRINCIPALID")
                .IsRequired()
                .ValueGeneratedOnAdd();
        }

    }

    public class DependentMap : IEntityTypeConfiguration<Dependent>
    {
        public void Configure(EntityTypeBuilder<Dependent> builder)
        {
            builder.ToTable("DEPENDENTS");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("DEPENDENTID")
                .IsRequired()
                .ValueGeneratedOnAdd();

            builder
                .HasOne(x => x.Principal)
                .WithOne(x => x.Dependent)
                .OnDelete(DeleteBehavior.Cascade)
                .HasForeignKey(typeof(Dependent), "PRINCIPALID");
        }

    }
}
