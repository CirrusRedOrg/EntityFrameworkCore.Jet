// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.



// ReSharper disable once CheckNamespace
namespace System.Collections.Generic
{
    [DebuggerStepThrough]
    internal static class EnumerableExtensions
    {
        public static string Join(this IEnumerable<object> source, string separator = ", ")
            => string.Join(separator, source);

        public static IEnumerable<T> Distinct<T>(
            this IEnumerable<T> source,
            Func<T?, T?, bool> comparer)
            where T : class
            => source.Distinct(new DynamicEqualityComparer<T>(comparer));

        private sealed class DynamicEqualityComparer<T>(Func<T?, T?, bool> func) : IEqualityComparer<T>
            where T : class
        {
            public bool Equals(T? x, T? y)
                => func(x, y);

            public int GetHashCode(T obj)
                => 0; // force Equals
        }
    }
}
