using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model23_NestedInclude
{
    public class DocumentMetadata
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<VariableMetadata> VariablesMetadata { get; set; } = new List<VariableMetadata>();
        public ICollection<DocumentMetadataExpression> DocumentMetadataExpressions { get; set; } = new List<DocumentMetadataExpression>();

        public override string ToString()
        {
            return string.Format("Metadata {0} ({1}, {2})", Id, VariablesMetadata.Count, DocumentMetadataExpressions.Count);
        }
    }
}
