using System.Collections.Generic;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model09
{
    public class One
    {
        public int Id { get; set; }
        public ICollection<Two> TwoList { get; set; }
    }
}