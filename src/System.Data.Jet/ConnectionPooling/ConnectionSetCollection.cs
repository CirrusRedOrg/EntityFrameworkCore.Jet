using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Data.Jet.ConnectionPooling
{
    class ConnectionSetCollection : KeyedCollection<string, ConnectionSet>
    {
        protected override string GetKeyForItem(ConnectionSet item)
        {
            return item.ConnectionString;
        }

        public bool TryGetValue(string key, out ConnectionSet connectionSet)
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

    }
}
