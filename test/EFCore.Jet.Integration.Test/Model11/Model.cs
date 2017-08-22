using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model11
{
    public abstract class DataCode
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }

    }

    public class InternCode : DataCode
    {
        public string PrimaryRelationalOperator { get; set; }
        public string PrimaryValue { get; set; }
        public string SecondaryRelationalOperator { get; set; }
        public string SecondaryValue { get; set; }

        public virtual ICollection<Version> Versions { get; set; }
    }

    public class Model : DataCode
    {
        public ICollection<string> Aliases { get; set; }
        public bool ExportOnly { get; set; }
        public virtual ICollection<Version> Versions { get; set; }
        public void GetOptions()
        {
            throw new NotImplementedException();
        }
    }

    public class Version
    {
        public long Id { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public float VersionNumber { get; set; }
        public DateTime EffectiveDate { get; set; }
        public virtual ICollection<Model> Models { get; set; }
        public virtual ICollection<InternCode> DataReleaseLevels { get; set; }
    }
}
