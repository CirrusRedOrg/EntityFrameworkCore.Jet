using System;
using System.Collections.Generic;
using System.Reflection;

namespace EntityFrameworkCore.Jet.Data
{
    internal static class TypesExtensions
    {
        internal static IEnumerable<Type> GetTypesInHierarchy(this Type type)
        {
            var currentType = type;

            while (currentType != null)
            {
                yield return currentType;

                currentType = currentType.BaseType;
            }
        }
    }
}