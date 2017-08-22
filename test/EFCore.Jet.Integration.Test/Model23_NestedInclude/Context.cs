using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model23_NestedInclude
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options) : base (options)
        {
        }

        // For migration test
        public Context()
        { }

        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentMetadata> DocumentMetadatas { get; set; }
        public DbSet<DocumentMetadataExpression> DocumentMetadataExps { get; set; }
        public DbSet<VariableMetadata> VariableMetadatas { get; set; }
        public DbSet<Expression> Expressions { get; set; }
    }
}
