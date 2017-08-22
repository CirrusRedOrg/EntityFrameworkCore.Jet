// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Jet.Infrastructure.Internal
{
    /// <summary>
    ///     Options set at the <see cref="IServiceProvider" /> singleton level to control Jet specific options.
    /// </summary>
    public interface IJetOptions : ISingletonOptions
    {
    }
}
