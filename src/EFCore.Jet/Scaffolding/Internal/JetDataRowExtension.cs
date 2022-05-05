using System.Data;
using JetBrains.Annotations;

namespace EntityFrameworkCore.Jet.Scaffolding.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public static class JetDataRowExtension
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static T? GetValueOrDefault<T>(this DataRow source, string name, T? defaultValue = default)
            => source.IsNull(name)
                ? defaultValue
                : (T)source[name];
    }
}