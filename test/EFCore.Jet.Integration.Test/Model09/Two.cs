using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model09
{
    public class Two
    {
        public int Id { get; set; }
        public int OneId { get; set; } 
        public virtual One One { get; set; }
        public virtual ICollection<Three> ThreeList { get; set; }
    }
}