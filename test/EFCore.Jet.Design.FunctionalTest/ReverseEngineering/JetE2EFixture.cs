using System;
using System.Data.Common;
using System.Data.Jet;
using System.Linq;
using System.Text.RegularExpressions;
using EntityFramework.Jet.FunctionalTests;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests.ReverseEngineering
{
    public class JetE2EFixture
    {
        public JetE2EFixture()
        {
            // The right script is in System.Data.Jet.Test
            //JetTestStore.GetOrCreateShared(FileName, () => { ExecuteScript();});
            JetTestStore.GetOrCreateShared(DatabaseName, () => { });
        }


        private const string ScriptPath = "E2E.sql";
        private const string DatabaseName = "E2E";
        private const string FileName = DatabaseName + ".accdb";

        public void ExecuteScript()
        {
            DbConnection connection = new JetConnection(JetConnection.GetConnectionString(FileName));

            var script = System.IO.File.ReadAllText(ScriptPath);
            foreach (var batch in
                new Regex("^GO", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(1000.0))
                    .Split(script).Where(b => !string.IsNullOrEmpty(b)))
            {
                DbCommand command = connection.CreateCommand();
                command.CommandText = batch;
                try
                {
                    command.ExecuteNonQuery();

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(batch);
                    throw;
                }
            }
        }

    }
}
