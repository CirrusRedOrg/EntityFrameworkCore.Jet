using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model23_NestedInclude
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            for (int i = 0; i < 1; i++)
            {

                DocumentMetadata documentMetadata = new DocumentMetadata()
                {
                    Name = "My document metadata"
                };
                documentMetadata.DocumentMetadataExpressions.Add(
                    new DocumentMetadataExpression()
                    {
                        Expression = new Expression() { Name = "Expression name", Value = "Expression Value" }
                    });
                documentMetadata.VariablesMetadata.Add(
                    new VariableMetadata() { DefaultValue = "Variable default value", Name = "Variable name", Type = "The type" });

                Document document = new Document()
                {
                    Name = "My new document",
                    DocumentMetadata = documentMetadata
                };
                Context.Documents.Add(document);
                if (i % 500 == 0)
                {
                    Context.SaveChanges();
                    base.DisposeContext();
                    base.CreateContext();
                    Console.Write(".");
                }
            }
            Context.SaveChanges();
            base.DisposeContext();
            base.CreateContext();
            {
                var documents = Context.Documents
                    .Include(db => db.DocumentMetadata)
                    .Include(db => db.DocumentMetadata.VariablesMetadata)
                    .Include(db => db.DocumentMetadata.DocumentMetadataExpressions).ToList();

                foreach (Document document in documents)
                {
                    Console.WriteLine(document);
                }

            }

        }

    }
}

