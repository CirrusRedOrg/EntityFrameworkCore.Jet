using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model34_JetEfBug
{
    public class Category
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public virtual ICollection<Item> Items { get; set; }
    }
    public class Item
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public virtual Category Category { get; set; }
    }
}
