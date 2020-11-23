using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model25_InheritTPT
{
    public class Company
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? DeactivatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public bool IsActive { get; set; }
    }

    public class Supplier : Company
    {
        public int ContactId { get; set; }
        public int DocumentId { get; set; }
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; }
    }
}
