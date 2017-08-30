// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDateTimeTypeMapping : DateTimeTypeMapping
    {
        private const string DateTimeFormatConst = "{0:MM/dd/yyyy HH:mm:ss}";
        private const string DateTimeShortFormatConst = "{0:MM/dd/yyyy}";

        /// <summary>
        ///     Initializes a new instance of the <see cref="JetDateTimeTypeMapping" /> class.
        /// </summary>
        /// <param name="storeType"> The name of the database type. </param>
        /// <param name="dbType"> The <see cref="System.Data.DbType" /> to be used. </param>
        public JetDateTimeTypeMapping(
            [NotNull] string storeType,
            DbType? dbType = null)
            : base(storeType, dbType)
        {
        }

        protected override void ConfigureParameter(DbParameter parameter)
        {
            base.ConfigureParameter(parameter);

            if (
                DbType == System.Data.DbType.Date ||
                DbType == System.Data.DbType.DateTime ||
                DbType == System.Data.DbType.DateTime2 ||
                DbType == System.Data.DbType.DateTimeOffset
                )
            {
                ((OleDbParameter)parameter).OleDbType = OleDbType.DBTimeStamp;
            }
        }

        /// <summary>
        ///     Creates a copy of this mapping.
        /// </summary>
        /// <param name="storeType"> The name of the database type. </param>
        /// <param name="size"> The size of data the property is configured to store, or null if no size is configured. </param>
        /// <returns> The newly created mapping. </returns>
        public override RelationalTypeMapping Clone(string storeType, int? size)
            => new JetDateTimeTypeMapping(storeType, DbType);

        /// <summary>
        ///     Gets the string format to be used to generate SQL literals of this type.
        /// </summary>
        protected override string SqlLiteralFormatString => "#" + DateTimeFormatConst + "#";


        public static string GenerateSqlLiteral(object o, bool shortForm)
        {
            return
                shortForm ? 
                string.Format("#" + DateTimeShortFormatConst + "#", o) : 
                string.Format("#" + DateTimeFormatConst + "#", o);

        }

    }
}
