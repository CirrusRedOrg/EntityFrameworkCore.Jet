using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model28
{
    static class Helpers
    {
        public static string Truncate(this string value, int length)
        {
            if (value.Length < length)
                return value;
            else
                return value.Substring(0, length);
        }
    }
}
