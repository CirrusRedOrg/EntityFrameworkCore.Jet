// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetTimeSpanTypeMapping : TimeSpanTypeMapping
    {
        public JetTimeSpanTypeMapping([NotNull] string storeType, DbType? dbType = null)
            : base(storeType, dbType)
        {
        }

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);

            // Workaround for a SQLClient bug
            if (DbType == System.Data.DbType.Time)
            {
                ((OleDbParameter)parameter).OleDbType = OleDbType.DBTimeStamp;
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            return string.Format("{0:#MM/dd/yyyy hh:mm:ss#}", JetConfiguration.TimeSpanOffset + (TimeSpan)value);
        }
    }
}
