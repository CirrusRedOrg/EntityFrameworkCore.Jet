// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : TimeSpanTypeMapping
    {
        public JetTimeSpanTypeMapping(
                [NotNull] string storeType)
            : base(storeType, System.Data.DbType.Time)
        {
        }

        protected JetTimeSpanTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetTimeSpanTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);

            if (DbType == System.Data.DbType.Date ||
                DbType == System.Data.DbType.DateTime ||
                DbType == System.Data.DbType.DateTime2 ||
                DbType == System.Data.DbType.DateTimeOffset ||
                DbType == System.Data.DbType.Time)
            {
                ((OleDbParameter) parameter).OleDbType = OleDbType.DBTimeStamp;
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            return $"{JetConfiguration.TimeSpanOffset + (TimeSpan) value:#MM'/'dd'/'yyyy hh\\:mm\\:ss#}";
        }
    }
}