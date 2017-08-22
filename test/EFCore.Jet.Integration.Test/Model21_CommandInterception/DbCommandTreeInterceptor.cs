using System;

namespace EFCore.Jet.Integration.Test.Model21_CommandInterception
{
    class DbCommandTreeInterceptor : IDbCommandTreeInterceptor
    {
        public void TreeCreated(DbCommandTreeInterceptionContext interceptionContext)
        {
            Console.WriteLine(interceptionContext.OriginalResult);
        }
    }
}
