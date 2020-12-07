// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : JetDateTimeTypeMapping
    {
        public JetDateTimeOffsetTypeMapping(
                [NotNull] string storeType)
            : base(
                storeType,
                System.Data.DbType.DateTime,
                typeof(DateTimeOffset)) // delibrately use DbType.DateTime, because OleDb will throw a
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
            // OLE DB can't handle the DateTimeOffset type.
            if (parameter.Value is DateTimeOffset dateTimeOffset)
            {
                parameter.Value = dateTimeOffset.UtcDateTime;
            }

            base.ConfigureParameter(parameter);
        }

        protected override string GenerateNonNullSqlLiteral(object value)
            => base.GenerateNonNullSqlLiteral(
                value is DateTimeOffset dateTimeOffset
                    ? dateTimeOffset.UtcDateTime
                    : value);
    }
}