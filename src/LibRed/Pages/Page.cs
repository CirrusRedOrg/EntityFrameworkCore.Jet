using System;
using System.Collections.Generic;
using System.Text;

namespace LibRed.Pages
{
    public abstract class Page
    {
        public byte PageType { get; internal set; }
        public int PageSize { get; set; }
        public virtual void ReadPage()
        {
        }

        public virtual void WritePage()
        {
        }
    }
}
