using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model01
{
    public class Standard
    {
        public Standard()
        {
            Students = new List<Student>();
        }

        // Index is supported only using fluent API
        /*[Index("MultipleColumnIndex", 2)]*/
        public int StandardId { get; set; }

        /*[Index("MultipleColumnIndex", 1)]*/
        public string StandardName { get; set; }
        public string Description { get; set; }

        public virtual ICollection<Student> Students { get; set; }
    }
}
