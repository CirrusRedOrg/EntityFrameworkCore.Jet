// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private readonly IJetOptions _options;
        private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
        public JetDateTimeOffsetTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(
                storeType, System.Data.DbType.String) // delibrately use DbType.DateTime, because OleDb will throw a
                                                      // "No mapping exists from DbType DateTimeOffset to a known OleDbType."
                                                      // exception when using DbType.DateTimeOffset.
        {
            _options = options;
        }

        protected JetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters)
        {
            _options = options;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeOffsetTypeMapping(parameters, _options);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            // OLE DB can't handle the DateTimeOffset type.
            if (parameter.Value is DateTimeOffset dateTimeOffset)
            {
                if (_options.DateTimeOffsetType == DateTimeOffsetType.SaveAsString)
                {
                    parameter.Value = dateTimeOffset.ToString("O");
                    parameter.DbType = System.Data.DbType.String;
                }
                else if (_options.DateTimeOffsetType == DateTimeOffsetType.SaveAsDateTime)
                {
                    parameter.Value = dateTimeOffset.DateTime;
                    parameter.DbType = System.Data.DbType.DateTime;
                }
                else if (_options.DateTimeOffsetType == DateTimeOffsetType.SaveAsDateTimeUtc)
                {
                    parameter.Value = dateTimeOffset.UtcDateTime;
                    parameter.DbType = System.Data.DbType.DateTime;
                }
            }

            base.ConfigureParameter(parameter);
        }

        protected override string SqlLiteralFormatString
            => DateTimeOffsetFormatConst;
    }
}