using System;
using System.Collections.Generic;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model01
{
    public class Standard
    {
        public Standard()
        {
            Students = [];
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
