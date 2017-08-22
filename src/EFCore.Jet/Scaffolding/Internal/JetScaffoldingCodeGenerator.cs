// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Scaffolding;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    public class JetScaffoldingCodeGenerator : IScaffoldingProviderCodeGenerator
    {
        public virtual string GenerateUseProvider(string connectionString, string language)
            => language == "CSharp"
                ? $".{nameof(JetDbContextOptionsExtensions.UseJet)}({GenerateVerbatimStringLiteral(connectionString)})"
                : null;

        private static string GenerateVerbatimStringLiteral(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
