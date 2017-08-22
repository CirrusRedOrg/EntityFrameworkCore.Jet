// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using Extensions.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Infrastructure.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetOptionsExtension : RelationalOptionsExtension
    {
        private string _logFragment;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetOptionsExtension()
        {
        }

        // NB: When adding new options, make sure to update the copy ctor below.

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected JetOptionsExtension([NotNull] JetOptionsExtension copyFrom)
            : base(copyFrom)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override RelationalOptionsExtension Clone()
            => new JetOptionsExtension(this);


        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool ApplyServices(IServiceCollection services)
        {
            Check.NotNull(services, nameof(services));

            services.AddEntityFrameworkJet();

            return true;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override string LogFragment
        {
            get
            {
                if (_logFragment == null)
                {
                    var builder = new StringBuilder();

                    builder.Append(base.LogFragment);

                    _logFragment = builder.ToString();
                }

                return _logFragment;
            }
        }
    }
}
