using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.Data.ConnectionPooling
{
    class ConnectionSetCollection : KeyedCollection<string, ConnectionSet>
    {
        protected override string GetKeyForItem(ConnectionSet item)
        {
            return item.ConnectionString;
        }

        // TryGetValue has been added in .NET Core 2.0.
        #pragma warning disable 109
        public new bool TryGetValue(string key, out ConnectionSet? connectionSet)
        {
            try
            {
                connectionSet = this[key];
                return true;
            }
            catch
            {
                connectionSet = null;
                return false;
            }
        }
        #pragma warning restore 109
    }
}
