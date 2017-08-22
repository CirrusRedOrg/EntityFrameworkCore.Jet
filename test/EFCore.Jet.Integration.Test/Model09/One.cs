using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model09
{
    public class One
    {
        public int Id { get; set; }
        public ICollection<Two> TwoList { get; set; }
    }
}