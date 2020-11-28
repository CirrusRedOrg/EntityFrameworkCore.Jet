using System;
using System.Collections.Generic;
using System.Reflection;

namespace EntityFrameworkCore.Jet.Data
{
    internal static class TypesExtensions
    {
        internal static IEnumerable<Type> GetTypesInHierarchy(this Type type)
        {
            while (type != null)
            {
                yield return type;

                type = type.GetTypeInfo().BaseType;
            }
        }
    }
}