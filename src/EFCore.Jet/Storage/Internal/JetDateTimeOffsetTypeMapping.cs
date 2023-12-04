// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Globalization;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
    {
        private readonly IJetOptions _options;
        private const string DateTimeOffsetFormatConst = @"'{0:yyyy-MM-ddTHH:mm:ss.fffffffzzz}'";
        private const string DateTimeFormatConst = @"'{0:yyyy-MM-dd HH:mm:ss}'";
        public JetDateTimeOffsetTypeMapping(
                [NotNull] string storeType,
                [NotNull] IJetOptions options)
            : base(
                storeType, options.DateTimeOffsetType == DateTimeOffsetType.SaveAsString ? System.Data.DbType.String : System.Data.DbType.DateTime)
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
                switch (_options.DateTimeOffsetType)
                {
                    case DateTimeOffsetType.SaveAsString:
                        parameter.Value = dateTimeOffset.ToString("O");
                        parameter.DbType = System.Data.DbType.String;
                        break;
                    case DateTimeOffsetType.SaveAsDateTime:
                        parameter.Value = dateTimeOffset.DateTime;
                        parameter.DbType = System.Data.DbType.DateTime;
                        break;
                    case DateTimeOffsetType.SaveAsDateTimeUtc:
                        parameter.Value = dateTimeOffset.UtcDateTime;
                        parameter.DbType = System.Data.DbType.DateTime;
                        break;
                }
            }

            base.ConfigureParameter(parameter);
        }

        protected override string SqlLiteralFormatString
            => DateTimeOffsetFormatConst;

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            if (value is not DateTimeOffset offset) return base.GenerateNonNullSqlLiteral(value);
            switch (_options.DateTimeOffsetType)
            {
                case DateTimeOffsetType.SaveAsString:
                    return base.GenerateNonNullSqlLiteral(offset);
                case DateTimeOffsetType.SaveAsDateTime:
                    {
                        var dateTime = offset.DateTime;
                        return string.Format(CultureInfo.InvariantCulture, DateTimeFormatConst, dateTime);
                    }
                case DateTimeOffsetType.SaveAsDateTimeUtc:
                    {
                        var dateTimeUtc = offset.DateTime;
                        return string.Format(CultureInfo.InvariantCulture, DateTimeFormatConst, dateTimeUtc);
                    }
                default:
                    return "";
            }
        }
    }
}