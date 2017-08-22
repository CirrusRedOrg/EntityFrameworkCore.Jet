// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFrameworkCore.Jet.Update.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetUpdateSqlGenerator : UpdateSqlGenerator, IJetUpdateSqlGenerator
    {
        public JetUpdateSqlGenerator(
            [NotNull] UpdateSqlGeneratorDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void AppendIdentityWhereCondition(StringBuilder commandStringBuilder, ColumnModification columnModification)
        {
            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, columnModification.ColumnName);
            commandStringBuilder.Append(" = ");

            commandStringBuilder.Append("@@identity");
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void AppendRowsAffectedWhereCondition(StringBuilder commandStringBuilder, int expectedRowsAffected)
        {
            // Jet does not support ROWCOUNT
            // Here we really hope that ROWCOUNT is not required
            commandStringBuilder
                .Append("1 = 1");

            /*

            commandStringBuilder
                .Append("@@ROWCOUNT = ")
                .Append(expectedRowsAffected.ToString(CultureInfo.InvariantCulture));
            */
        }
    }
}
