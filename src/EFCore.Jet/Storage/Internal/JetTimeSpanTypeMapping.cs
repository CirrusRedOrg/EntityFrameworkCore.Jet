// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Data;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : JetDateTimeTypeMapping
    {
        public JetTimeSpanTypeMapping(
                [NotNull] string storeType)
            : base(storeType, System.Data.DbType.Time, typeof(TimeSpan))
        {
        }

        protected JetTimeSpanTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeSpanTypeMapping(parameters);
        
        protected override string GenerateNonNullSqlLiteral(object value)
            => base.GenerateNonNullSqlLiteral(JetConfiguration.TimeSpanOffset + (TimeSpan) value);
    }
}