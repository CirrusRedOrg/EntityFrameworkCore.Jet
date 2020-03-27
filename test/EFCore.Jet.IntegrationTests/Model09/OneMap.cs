using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model09
{
    public class OneMap : IEntityTypeConfiguration<One>
    {
        public void Configure(EntityTypeBuilder<One> builder)
        {
            builder.HasKey(t => t.Id);
            builder.ToTable("One");
        }

    }
}