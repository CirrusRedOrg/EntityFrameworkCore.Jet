using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EFCore.Jet.Integration.Test.Model36_DetachedScenario
{
    public class Holder
    {
        public int Id { get; set; }
        [MaxLength(50)]
        public string Some { get; set; }
        [MaxLength(50)]
        [Required]
        public string Some2 { get; set; }
        public Thing Thing { get; set; }

        public override string ToString()
        {
            return string.Format("Id:{0}, Some:{1}, Thing:{2}", Id, Some, Thing);
        }
    }

    public class Thing
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public virtual ICollection<Holder> Holders { get; set; }

        public override string ToString()
        {
            return string.Format("Id:{0}, Name:{1}", Id, Name);
        }

    }
}
