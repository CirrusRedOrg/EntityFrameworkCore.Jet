// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : JetDateTimeTypeMapping
    {
        private readonly IJetOptions _options;

        public JetDateTimeOffsetTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(
                storeType,
                options,
                System.Data.DbType.DateTime,
                typeof(DateTimeOffset)) // delibrately use DbType.DateTime, because OleDb will throw a
                                        // "No mapping exists from DbType DateTimeOffset to a known OleDbType."
                                        // exception when using DbType.DateTimeOffset.
        {
            _options = options;
        }

        protected JetDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters, IJetOptions options)
            : base(parameters, options)
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
                parameter.Value = dateTimeOffset.UtcDateTime;
            }

            base.ConfigureParameter(parameter);
        }

        protected override DateTime ConvertToDateTimeCompatibleValue(object value)
            => ((DateTimeOffset) value).UtcDateTime;
    }
}