// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private const string DateTimeOffsetFormatConst = @"{0:MM'/'dd'/'yyyy HH\:mm\:ss}";

        public JetDateTimeOffsetTypeMapping(
                [NotNull] string storeType)
            : base(
                storeType,
                System.Data.DbType.DateTime) // delibrately use DbType.DateTime, because OleDb will throw a
                                             // "No mapping exists from DbType DateTimeOffset to a known OleDbType."
                                             // exception when using DbType.DateTimeOffset.
        {
        }

        protected JetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeOffsetTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);

            // Check: Is this really necessary for Jet?
            /*
            if (DbType == System.Data.DbType.Date ||
                DbType == System.Data.DbType.DateTime ||
                DbType == System.Data.DbType.DateTime2 ||
                DbType == System.Data.DbType.DateTimeOffset ||
                DbType == System.Data.DbType.Time)
            {
                ((OleDbParameter) parameter).OleDbType = OleDbType.DBTimeStamp;
            }
            */

            // OLE DB can't handle the DateTimeOffset type.
            if (parameter.Value is DateTimeOffset dateTimeOffset)
            {
                parameter.Value = dateTimeOffset.UtcDateTime;
            }
        }

        protected override string SqlLiteralFormatString => "#" + DateTimeOffsetFormatConst + "#";

        public override string GenerateProviderValueSqlLiteral([CanBeNull] object value)
            => value == null
                ? "NULL"
                : GenerateNonNullSqlLiteral(
                    value is DateTimeOffset dateTimeOffset
                        ? dateTimeOffset.UtcDateTime
                        : value);
    }
}