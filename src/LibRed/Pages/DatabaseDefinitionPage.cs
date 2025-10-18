using System;
using System.Collections.Generic;
using System.Text;

namespace LibRed.Pages
{
    public class DatabaseDefinitionPage : Page
    {
        public byte JetVersion { get; internal set; }
        public string DatabasePassword { get; set; }

        public int DatabaseKey { get; set; }
        public short CodePage { get; set; }
        public short TextCollateSortOrder { get; set; }
        public string PageKey { get; set; }
        public DateTime DatabaseCreationDate { get; set; }
        public string CreateProgramName { get; set; }
        public DatabaseDefinitionPage()
        {
            PageType = 0x00;
        }
    }
}
