using System;

namespace EFCore.Jet.Integration.Test.Model72_TableSplitting
{

    //Mapped to a table, has foreign key (eg. customerId)
    public class Product
    {
        public int Id { get; set; }

        public string Description { get; set; }
        public ProductDetails Details { get; set; }
    }

    public class ProductDetails
    {
        public int Id { get; set; }

        public string MoreDescription { get; set; }
        public Product Product { get; set; }
    }

}
