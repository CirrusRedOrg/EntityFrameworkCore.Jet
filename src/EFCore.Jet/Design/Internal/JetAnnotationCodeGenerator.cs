// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;

namespace EntityFrameworkCore.Jet.Design.Internal
{
    public class JetAnnotationCodeGenerator : AnnotationCodeGenerator
    {
        public JetAnnotationCodeGenerator([NotNull] AnnotationCodeGeneratorDependencies dependencies)
            : base(dependencies)
        {
        }

        public override bool IsHandledByConvention(IModel model, IAnnotation annotation)
        {
            Check.NotNull(model, nameof(model));
            Check.NotNull(annotation, nameof(annotation));

            if (annotation.Name == RelationalAnnotationNames.DefaultSchema)
                return true;

            return false;
        }

        public override string GenerateFluentApi(IKey key, IAnnotation annotation, string language)
        {
            Check.NotNull(key, nameof(key));
            Check.NotNull(annotation, nameof(annotation));
            Check.NotNull(language, nameof(language));

            return annotation.Name == JetAnnotationNames.Clustered && language == "CSharp"
                ? $".{nameof(JetIndexBuilderExtensions.ForJetIsClustered)}({((bool)annotation.Value == false ? "false" : "")})"
                : null;
        }

        public override string GenerateFluentApi(IIndex index, IAnnotation annotation, string language)
        {
            Check.NotNull(index, nameof(index));
            Check.NotNull(annotation, nameof(annotation));
            Check.NotNull(language, nameof(language));

            return annotation.Name == JetAnnotationNames.Clustered && language == "CSharp"
                ? $".{nameof(JetIndexBuilderExtensions.ForJetIsClustered)}({((bool)annotation.Value == false ? "false" : "")})"
                : null;
        }
    }
}
