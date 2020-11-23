using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model23_NestedInclude
{
    public class Document
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DocumentMetadata DocumentMetadata { get; set; }

        public override string ToString()
        {
            return string.Format("Document {0} {1}=> {2}", Id, Name, DocumentMetadata);
        }
    }
}
