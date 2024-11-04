using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model28
{
    static class Helpers
    {
        public static string Truncate(this string value, int length)
        {
            return value.Length < length ? value : value[..length];
        }
    }
}
