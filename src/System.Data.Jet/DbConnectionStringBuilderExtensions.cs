using System.Data.Common;

namespace System.Data.Jet
{
    public static class DbConnectionStringBuilderExtensions
    {
        private const string ProviderKey = "Provider";

        public static string GetProvider(this DbConnectionStringBuilder source)
            => source.TryGetValue(ProviderKey, out var providerName)
                ? providerName as string
                : null;

        public static void SetProvider(this DbConnectionStringBuilder source, string value)
            => source[ProviderKey] = value;
    }
}