// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    public class JetDateTimeTypeMapping : DateTimeTypeMapping
    {
        private const string DateTimeFormatConst = @"{0:MM'/'dd'/'yyyy HH\:mm\:ss}";
        private const string DateTimeShortFormatConst = "{0:MM'/'dd'/'yyyy}";

        public JetDateTimeTypeMapping(
            [NotNull] string storeType,
            DbType? dbType = null)
            : base(storeType, dbType)
        {
        }

        protected JetDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new JetDateTimeTypeMapping(parameters);

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
        }

        protected override string SqlLiteralFormatString => "#" + DateTimeFormatConst + "#";

        public static string GenerateSqlLiteral([NotNull] object o, bool shortForm)
        {
            if (o == null)
                throw new ArgumentNullException(nameof(o));

            return
                shortForm
                    ? string.Format("#" + DateTimeShortFormatConst + "#", o)
                    : string.Format("#" + DateTimeFormatConst + "#", o);
        }
    }
}