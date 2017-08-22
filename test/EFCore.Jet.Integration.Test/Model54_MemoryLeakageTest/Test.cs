using System;
using System.Data.Common;
using System.Data.Jet;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model54_MemoryLeakageTest
{
    public abstract class Test : TestBase<Context>
    {
        //[TestMethod]
        public void Run()
        {
            bool oldJetShowSqlStatements = JetConnection.ShowSqlStatements;
            JetConnection.ShowSqlStatements = false;

            DbConnection connection = GetConnection();


            for (int i = 0; i < 100; i++)
            {
                base.CreateContext();

                for (int j = 0; j < 50; j++)
                {

                    Student student = new Student()
                    {
                        StudentName = string.Format("Student name {0}", i * 100 + j),
                        Notes = string.Format("Student notes {0}", i * 100 + j),
                        Standard = new Standard() { StandardName = string.Format("Standard of student {0}", i * 100 + j) }
                    };

                    Context.Students.Add(student);
                }

                Context.SaveChanges();

            }

            Console.WriteLine("Connection state {0}", connection.State);
            connection.Open();

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            PrintUsedMemory();
            long usedMemory = GetUsedMemory();

            for (int j = 0; j < 5; j++)
            {

                for (int i = 0; i < 15; i++)
                {
                    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                    Context.Students.ToList();
                    //Context.Dispose();
                }

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                PrintUsedMemory();
            }

            connection.Dispose();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

            Assert.IsFalse(GetUsedMemory()-usedMemory > 10000000, "Memory leakage");

            JetConnection.ShowSqlStatements = oldJetShowSqlStatements;

        }

        private static void PrintUsedMemory()
        {
            long usedMemory = GetUsedMemory();

            Console.WriteLine("Used memory {0}", usedMemory);
        }

        private static long GetUsedMemory()
        {
            Process proc = Process.GetCurrentProcess();
            long usedMemory = proc.WorkingSet64;
            return usedMemory;
        }
    }
}
