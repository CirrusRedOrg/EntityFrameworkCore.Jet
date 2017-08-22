using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model50_Interception
{
    static class LanguageExtensions
    {
        public static bool In<T>(this T source, params T[] list)
        {
            return (list as IList<T>).Contains(source);
        }
    }
}
