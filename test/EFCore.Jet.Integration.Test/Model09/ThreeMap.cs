using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model09
{
    public class ThreeMap : IEntityTypeConfiguration<Three>
    {

        public void Configure(EntityTypeBuilder<Three> builder)
        {
            builder.HasKey(t => new { t.Id, t.OneId, t.TwoId });
            builder.ToTable("Three");

            builder.HasOne(t => t.Two).WithMany(t => t.ThreeList).HasForeignKey(t => new {t.OneId, t.TwoId}).IsRequired();
        }
    }
}