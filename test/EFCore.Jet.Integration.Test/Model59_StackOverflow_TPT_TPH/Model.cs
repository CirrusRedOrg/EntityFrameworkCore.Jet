using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCore.Jet.Integration.Test.Model59_StackOverflow_TPT_TPH
{
    public abstract class BasePoco
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
    }


    [Table("Activities")]
    public class Activity : BasePoco
    {
        public ActivityType ActivityType { get; set; }

        [MaxLength(1000)]
        public string Description { get; set; }
    }

    public class DataCaptureActivityBase : Activity
    {

        [MaxLength(100)]
        public string Title { get; set; }
    }

    [Table("DataCaptureActivities")]
    public class DataCaptureActivity : DataCaptureActivityBase
    {
        public virtual DataCaptureActivityType DataCaptureActivityType { get; set; }
    }

    [Table("MasterDataCaptureActivities")]
    public class MasterDataCaptureActivity : DataCaptureActivityBase
    {
        public virtual string SomeOtherField { get; set; }
    }

    public enum ActivityType
    {
        A,
        B,
        C
    }

    public enum DataCaptureActivityType
    {
        D,
        E,
        F
    }


}
