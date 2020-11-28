using System;

namespace EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition
{
    class Column
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Nullable { get; set; }
        public int? MaxLength { get; set; }

        public override string ToString()
        {
            return string.Format("{0} {2}({3}) {1}", Name, Nullable ? "Null" : "NotNull", Type, MaxLength);
        }
    }
}
