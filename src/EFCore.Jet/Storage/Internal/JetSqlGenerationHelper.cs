// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetSqlGenerationHelper : RelationalSqlGenerationHelper
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetSqlGenerationHelper(
            RelationalSqlGenerationHelperDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override string EscapeIdentifier(string identifier)
        {
            Check.NotEmpty(identifier, nameof(identifier));

            identifier = identifier
                .Replace(".", "#");

            return identifier;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void EscapeIdentifier(StringBuilder builder, string identifier)
        {
            Check.NotEmpty(identifier, nameof(identifier));

            identifier = identifier
                .Replace(".", "#");

            builder.Append(identifier);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override string DelimitIdentifier(string? identifier)
        {
            return $"`{EscapeIdentifier(TruncateIdentifier(Check.NotEmpty(identifier, nameof(identifier))))}`";
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void DelimitIdentifier(StringBuilder builder, string identifier)
        {
            Check.NotEmpty(identifier, nameof(identifier));

            builder.Append('`');
            EscapeIdentifier(builder, TruncateIdentifier(identifier));
            builder.Append('`');
        }

        public override void DelimitIdentifier(StringBuilder builder, string name, string? schema)
        {
            // Schema is not supported in Jet
            DelimitIdentifier(builder, name);
        }

        public override string DelimitIdentifier(string name, string? schema)
        {
            // Schema is not supported in Jet
            return DelimitIdentifier(Check.NotEmpty(name, nameof(name)));
        }

        public static string TruncateIdentifier(string? identifier)
        {
            if (identifier?.Length <= 64)
                return identifier;

            return identifier?.Substring(0, 56) + identifier?.ToLowerInvariant().GetHashCode().ToString("X8");
        }
    }
}
