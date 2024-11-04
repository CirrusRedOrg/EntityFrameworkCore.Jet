using System;
using System.Collections.Generic;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model54_MemoryLeakageTest
{
    public class Standard
    {
        public Standard()
        {
            Students = [];
        }

        // Index supported only in fluent api
        /* [Index("MultipleColumnIndex", 2)]*/
        public int StandardId { get; set; }
        /*[Index("MultipleColumnIndex", 1)]*/
        public string StandardName { get; set; }
        public string Description { get; set; }

        public virtual ICollection<Student> Students { get; set; }
    }
}
