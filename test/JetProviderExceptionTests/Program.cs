using System;

namespace JetProviderExceptionTests
{
    internal static class Program
    {
        private static void Main()
        {
            Console.WriteLine($"Running as {(Environment.Is64BitProcess ? "x64" : "x86")} process.");
            
            var tagDBPARAMBINDINFOName = "tagDBPARAMBINDINFO" + (Environment.Is64BitProcess
                ? string.Empty
                : "_x86");
            
            // Is 2 on x86 and 8 on x64.
            Console.WriteLine($"{tagDBPARAMBINDINFOName} field alignment is {Type.GetType($"System.Data.OleDb.{tagDBPARAMBINDINFOName}, System.Data.OleDb").StructLayoutAttribute.Pack}.");
            
            // Is 8 on both x86 and x64.
            Console.WriteLine($"tagDBPARAMS field alignment is {Type.GetType("System.Data.OleDb.tagDBPARAMS, System.Data.OleDb").StructLayoutAttribute.Pack}.");

            Console.WriteLine();
            //Console.WriteLine("Press any key to start...");
            //Console.ReadKey();

            //var northwindTest = new NorthwindTestOleDbCommand();
            //northwindTest.Run();
            
            var iceCreamTest = new IceCreamTest();
            iceCreamTest.Run();
        }
    }
}