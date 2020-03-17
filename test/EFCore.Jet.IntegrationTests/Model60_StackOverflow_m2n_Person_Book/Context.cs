using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

// https://stackoverflow.com/questions/44925080/compare-c-sharp-entity-framework-add-or-remove-multiple-records-without-manual-c

namespace EFCore.Jet.Integration.Test.Model60_StackOverflow_m2n_Person_Book
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        public DbSet<Book> Books { get; set; }
        public DbSet<Person> People { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Person>()
                .HasMany<Book>(user => user.OwnedBooks)
                .WithMany(book => book.Owners)
                .Map(mapping =>
                {
                    mapping.MapLeftKey("PersonId");
                    mapping.MapRightKey("BookId");
                    mapping.ToTable("PersonBook");
                });
        }
    }

}
