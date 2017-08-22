// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;

namespace EntityFrameworkCore.Jet.Metadata.Conventions.Internal
{
    public class JetDbFunctionConvention : RelationalDbFunctionConvention
    {
        // No [default] schema for Jet
        /*
        protected override void Apply(InternalModelBuilder modelBuilder, string name, Annotation annotation)
        {
            base.Apply(modelBuilder, name, annotation);

            ((DbFunction)annotation.Value).DefaultSchema = "dbo";
        }
        */
    }
}
