// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Infrastructure.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class JetOptionsExtension : RelationalOptionsExtension
    {
        private DbContextOptionsExtensionInfo _info;

        // private bool? _rowNumberPaging;
        private DbProviderFactory _dataAccessProviderFactory;
        private bool _useOuterSelectSkipEmulationViaDataReader;
        private bool _enableMillisecondsSupport;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public JetOptionsExtension()
        {
        }

        // NB: When adding new options, make sure to update the copy ctor below.

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected JetOptionsExtension([NotNull] JetOptionsExtension copyFrom)
            : base(copyFrom)
        {
            // _rowNumberPaging = copyFrom._rowNumberPaging;
            _dataAccessProviderFactory = copyFrom._dataAccessProviderFactory;
            _useOuterSelectSkipEmulationViaDataReader = copyFrom._useOuterSelectSkipEmulationViaDataReader;
            _enableMillisecondsSupport = copyFrom._enableMillisecondsSupport;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override DbContextOptionsExtensionInfo Info
            => _info ??= new ExtensionInfo(this);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override RelationalOptionsExtension Clone()
            => new JetOptionsExtension(this);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        // public virtual bool? RowNumberPaging => _rowNumberPaging;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        /*
        public virtual JetOptionsExtension WithRowNumberPaging(bool rowNumberPaging)
        {
            var clone = (JetOptionsExtension) Clone();

            // clone._rowNumberPaging = rowNumberPaging;

            return clone;
        }
        */

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DbProviderFactory DataAccessProviderFactory => _dataAccessProviderFactory;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual JetOptionsExtension WithDataAccessProviderFactory(DbProviderFactory dataAccessProviderFactory)
        {
            var clone = (JetOptionsExtension) Clone();

            clone._dataAccessProviderFactory = dataAccessProviderFactory;

            return clone;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool UseOuterSelectSkipEmulationViaDataReader => _useOuterSelectSkipEmulationViaDataReader;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual JetOptionsExtension WithUseOuterSelectSkipEmulationViaDataReader(bool enabled)
        {
            var clone = (JetOptionsExtension) Clone();

            clone._useOuterSelectSkipEmulationViaDataReader = enabled;

            return clone;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool EnableMillisecondsSupport => _enableMillisecondsSupport;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual JetOptionsExtension WithEnableMillisecondsSupport(bool enabled)
        {
            var clone = (JetOptionsExtension) Clone();

            clone._enableMillisecondsSupport = enabled;

            return clone;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override void ApplyServices(IServiceCollection services)
            => services.AddEntityFrameworkJet();

        private sealed class ExtensionInfo : RelationalExtensionInfo
        {
            private int? _serviceProviderHash;
            private string _logFragment;

            public ExtensionInfo(IDbContextOptionsExtension extension)
                : base(extension)
            {
            }

            private new JetOptionsExtension Extension
                => (JetOptionsExtension) base.Extension;

            public override bool IsDatabaseProvider => true;

            public override string LogFragment
            {
                get
                {
                    if (_logFragment == null)
                    {
                        var builder = new StringBuilder();

                        builder.Append(base.LogFragment);

                        /*
                        if (Extension._rowNumberPaging == true)
                        {
                            builder.Append("RowNumberPaging ");
                        }
                        */

                        if (Extension._dataAccessProviderFactory != null)
                        {
                            builder.Append("DataAccessProviderFactory ");
                        }

                        if (Extension._useOuterSelectSkipEmulationViaDataReader)
                        {
                            builder.Append("UseOuterSelectSkipEmulationViaDataReader ");
                        }

                        if (Extension._enableMillisecondsSupport)
                        {
                            builder.Append("EnableMillisecondsSupport ");
                        }

                        _logFragment = builder.ToString();
                    }

                    return _logFragment;
                }
            }

            public override int GetServiceProviderHashCode()
            {
                if (_serviceProviderHash == null)
                {
                    _serviceProviderHash = (base.GetServiceProviderHashCode() * 397) ^
                                           (Extension._dataAccessProviderFactory?.GetHashCode() ?? 0) ^
                                           (Extension._useOuterSelectSkipEmulationViaDataReader.GetHashCode() * 397) ^
                                           (Extension._enableMillisecondsSupport.GetHashCode() * 397)/* ^
                                           (Extension._rowNumberPaging?.GetHashCode() ?? 0L)*/;
                }

                return _serviceProviderHash.Value;
            }

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                /*
                debugInfo["Jet:" + nameof(JetDbContextOptionsBuilder.UseRowNumberForPaging)]
                    = (Extension._rowNumberPaging?.GetHashCode() ?? 0L).ToString(CultureInfo.InvariantCulture);
                */
                debugInfo["Jet:" + nameof(DataAccessProviderFactory)]
                    = (Extension._dataAccessProviderFactory?.GetHashCode() ?? 0L).ToString(CultureInfo.InvariantCulture);
#pragma warning disable 618
                debugInfo["Jet:" + nameof(JetDbContextOptionsBuilder.UseOuterSelectSkipEmulationViaDataReader)]
#pragma warning restore 618
                    = Extension._useOuterSelectSkipEmulationViaDataReader.GetHashCode().ToString(CultureInfo.InvariantCulture);
                debugInfo["Jet:" + nameof(JetDbContextOptionsBuilder.EnableMillisecondsSupport)]
                    = Extension._enableMillisecondsSupport.GetHashCode().ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}