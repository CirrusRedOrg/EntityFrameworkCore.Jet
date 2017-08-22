using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model62_InnerQueryBug_DbInitializerSeed
{
    [Table("Items62")]
    public class Item
    {
        public int ItemId { get; set; }
        public string Name { get; set; }
        public bool IsService { get; set; }

        public virtual ICollection<SaleDetail> SaleDetails { get; set; }
    }

    public class Sale
    {
        public int SaleId { get; set; }
        public string SaleNo { get; set; }

        public virtual ICollection<SaleDetail> SaleDetails { get; set; }
    }

    public class SaleDetail
    {
        public int SaleDetailId { get; set; }
        public float Quantity { get; set; }
        public decimal Rate { get; set; }

        #region FK
        public int ItemId { get; set; }
        public int SaleId { get; set; }
        #endregion

        #region NP
        public virtual Item Item { get; set; }
        public virtual Sale Sale { get; set; }
        #endregion
    }
}
