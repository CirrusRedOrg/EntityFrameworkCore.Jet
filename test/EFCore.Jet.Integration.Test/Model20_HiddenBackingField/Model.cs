using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFCore.Jet.Integration.Test.Model20_HiddenBackingField
{
    public interface IEntity
    {
        int Id { get; set; }
        State State { get; set; }
    }

    public abstract class Entity : IEntity
    {
        protected int InternalState { get; set; }
        public int Id { get; set; }

        [NotMapped]
        public State State
        {
            get { return (State) InternalState; }
            set { InternalState = (int) value; }
        }

        // Entity is not a POCO class because of this :(
        // If we want to hide InternalState this is the only way to map it
        public class EntityMap : IEntityTypeConfiguration<Entity>
        {
            public void Configure(EntityTypeBuilder<Entity> builder)
            {
                // Properties
                builder.Property(t => t.InternalState)
                    .HasColumnName("State");
            }
        }
    }

    public enum State
    {
        Inactive = 0,
        Active = 1
    }

    public class Company : Entity
    {
        [NotMapped]
        public new CompanyState State
        {
            get { return (CompanyState)InternalState; }
            set { InternalState = (int)value; }
        }
        [MaxLength(50)]
        public string SomeOtherProp { get; set; }
    }

    public class Employee : Entity
    {
        [MaxLength(50)]
        public string SomeOtherProp { get; set; }
    }

    public enum CompanyState
    {
        Inactive = 0,
        Active = 1,
        Extra = 2
    }

}
