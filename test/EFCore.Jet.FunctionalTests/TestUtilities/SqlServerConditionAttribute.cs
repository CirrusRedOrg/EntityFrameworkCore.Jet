// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit.Sdk;

namespace EntityFramework.Jet.FunctionalTests.TestUtilities
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    [TraitDiscoverer("Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests.Utilities.SqlServerConditionTraitDiscoverer", "Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests")]
    public class SqlServerConditionAttribute : Attribute, ITestCondition, ITraitAttribute
    {
        public SqlServerCondition Conditions { get; set; }

        public SqlServerConditionAttribute(SqlServerCondition conditions)
        {
            Conditions = conditions;
        }

        public bool IsMet
        {
            get
            {
                var isMet = true;
                if (Conditions.HasFlag(SqlServerCondition.SupportsSequences))
                {
                    return false;
                }
                if (Conditions.HasFlag(SqlServerCondition.SupportsOffset))
                {
                    return true;
                }
                if (Conditions.HasFlag(SqlServerCondition.IsSqlAzure))
                {
                    isMet = false;
                }
                return isMet;
            }
        }

        public string SkipReason =>
            string.Format("The test Jet does not meet these conditions: '{0}'"
                , string.Join(", ", Enum.GetValues(typeof(SqlServerCondition))
                    .Cast<Enum>()
                    .Where(f => Conditions.HasFlag(f))
                    .Select(f => Enum.GetName(typeof(SqlServerCondition), f))));
    }

    [Flags]
    public enum SqlServerCondition
    {
        SupportsSequences = 1 << 0,
        SupportsOffset = 1 << 1,
        IsSqlAzure = 1 << 2,
        IsNotSqlAzure = 1 << 3,
    }
}
