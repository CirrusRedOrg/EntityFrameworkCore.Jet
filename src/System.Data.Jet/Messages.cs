using System;

namespace System.Data.Jet
{
    static class Messages
    {
        public static string CannotChangePropertyValueInThisConnectionState(string propertyName, ConnectionState state)
        {
            return $"Cannot modify \"{propertyName}\" property in this connection state. Current connection state is {state}";
        }

        public static string CannotReadPropertyValueInThisConnectionState(string propertyName, ConnectionState state)
        {
            return $"Cannot read \"{propertyName}\" property in this connection state. Current connection state is {state}";
        }

        public static string CannotCallMethodInThisConnectionState(string methodName, ConnectionState requiredState, ConnectionState state)
        {
            return $"\"{methodName}\" requires a connection in {requiredState} state. Current connection state is {state}";
        }

        public static string CannotCallMethodInThisConnectionState(string methodName, ConnectionState state)
        {
            return $"Cannot call method \"{methodName}\" in this connection state. Current connection state is {state}";
        }



        public static string MethodUnsupportedByJet(string methodName)
        {
            return $"\"{methodName}\" is not supported by Jet";
        }

        public static string PropertyNotInitialized(string propertyName)
        {
            return $"\"{propertyName}\" not initialized";
        }

        public static string UnsupportedParallelTransactions()
        {
            return "JetConnection does not support parallel transactions";
        }
    }
}
