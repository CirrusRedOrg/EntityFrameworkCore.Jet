using System;
using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EntityFrameworkCore.Jet.Data
{
    // A small wrapper around COM interop to make it more easy to use.
    // See https://github.com/dotnet/runtime/issues/12587#issuecomment-534611966
    internal class ComObject : DynamicObject, IDisposable
    {
        private object _instance;
#if DEBUG
        private readonly Guid _trackingId = Guid.NewGuid();
#endif

        public static ComObject CreateFirstFrom(params string[] progids)
        {
            foreach (var progid in progids)
            {
                var type = Type.GetTypeFromProgID(progid, false);
                if (type != null)
                {
                    var instance = Activator.CreateInstance(type);
                    if (instance != null)
                    {
                        return new ComObject(instance);
                    }
                }
            }
            
            throw new TypeLoadException("Could not create an instance using any of the supplied ProgIDs.");
        }
        
        public static ComObject CreateFirstFrom(params Guid[] clsids)
        {
            foreach (var clsid in clsids)
            {
                var type = Type.GetTypeFromCLSID(clsid, false);
                if (type != null)
                {
                    var instance = Activator.CreateInstance(type);
                    if (instance != null)
                    {
                        return new ComObject(instance);
                    }
                }
            }
            
            throw new TypeLoadException("Could not create an instance using any of the supplied CLSIDs.");
        }

        public ComObject(object instance)
        {
            if (instance is ComObject)
            {
                throw new ArgumentException("The object is already a ComObject.", nameof(instance));
            }
            
            _instance = instance;
        }

        public ComObject(string progid)
            : this(Activator.CreateInstance(Type.GetTypeFromProgID(progid, true)))
        {
        }

        public ComObject(Guid clsid)
            : this(Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true)))
        {
        }

        public object Detach()
        {
            var instance = _instance;
            _instance = null;
            return instance;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            result = WrapIfRequired(
                _instance.GetType()
                    .InvokeMember(
                        binder.Name,
                        BindingFlags.GetProperty,
                        Type.DefaultBinder,
                        _instance,
                        new object[0]
                    ));
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            _instance.GetType()
                .InvokeMember(
                    binder.Name,
                    BindingFlags.SetProperty,
                    Type.DefaultBinder,
                    _instance,
                    new[]
                    {
                        value is ComObject comObject
                            ? comObject._instance
                            : value
                    }
                );
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            result = WrapIfRequired(
                _instance.GetType()
                    .InvokeMember(
                        binder.Name,
                        BindingFlags.InvokeMethod,
                        Type.DefaultBinder,
                        _instance,
                        args
                    ));
            return true;
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            // This should work for all specific interfaces derived from `_Collection` (like `_Tables`) in ADOX.
            result = WrapIfRequired(
                _instance.GetType()
                    .InvokeMember(
                        "Item",
                        BindingFlags.GetProperty,
                        Type.DefaultBinder,
                        _instance,
                        indexes
                    ));
            return true;
        }

        // See https://github.com/dotnet/runtime/issues/12587#issuecomment-578431424
        private static object WrapIfRequired(object obj)
            => obj != null &&
               obj.GetType().IsCOMObject
                ? new ComObject(obj)
                : obj;

        public void Dispose()
        {
            // The RCW is a .NET object and cannot be released from the finalizer,
            // because it might not exist anymore.
            if (_instance != null)
            {
                Marshal.ReleaseComObject(_instance);
                _instance = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}