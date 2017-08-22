using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model23_NestedInclude
{
    public class DocumentMetadata
    {
        public DocumentMetadata()
        {
            VariablesMetadata = new List<VariableMetadata>();
            DocumentMetadataExpressions = new List<DocumentMetadataExpression>();
        }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ICollection<VariableMetadata> VariablesMetadata { get; set; }
        public ICollection<DocumentMetadataExpression> DocumentMetadataExpressions { get; set; }

        public override string ToString()
        {
            return string.Format("Metadata {0} ({1}, {2})", Id, VariablesMetadata.Count, DocumentMetadataExpressions.Count);
        }
    }
}
