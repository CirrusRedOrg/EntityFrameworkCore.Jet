using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model09
{
    public class TwoMap : IEntityTypeConfiguration<Two>
    {
        public void Configure(EntityTypeBuilder<Two> builder)
        {
            builder.HasKey(t => new { t.Id, t.OneId });
            builder.ToTable("Two");

            //builder.HasRequired(t => t.One).WithMany(t => t.TwoList).HasForeignKey(t => t.OneId);

            // Now has required is obtained setting OneId as required (not nullable)
            builder.HasOne(t => t.One).WithMany(t => t.TwoList).HasForeignKey(t => t.OneId);

        }


    }
}