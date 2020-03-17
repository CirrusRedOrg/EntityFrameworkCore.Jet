using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPT
{
    public class CompanyMap : IEntityTypeConfiguration<Company>
    {
        public void Configure(EntityTypeBuilder<Company> builder)
        {
            // Primary Key
            builder.HasKey(c => c.Id);

            //Table  
            builder.ToTable("Company");

        }

    }

    public class SupplierMap : IEntityTypeConfiguration<Supplier>
    {
        public void Configure(EntityTypeBuilder<Supplier> builder)
        {

            // Properties

            //Relationship
            /*builder.HasRequired(s => s.Company)
                .WithMany().HasForeignKey(c => c.CompanyId);*/
            builder.HasOne(s => s.Company)
                .WithMany().HasForeignKey(c => c.CompanyId);


            //Table  
            builder.ToTable("Supplier");

        }

    }

}
