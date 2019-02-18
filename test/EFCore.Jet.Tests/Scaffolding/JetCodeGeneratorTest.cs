// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests.Scaffolding
{
    public class SqlServerCodeGeneratorTest
    {
        [Fact]
        public virtual void Use_provider_method_is_generated_correctly()
        {
            var codeGenerator = new JetCodeGenerator(
                new ProviderCodeGeneratorDependencies(
                    Enumerable.Empty<IProviderCodeGeneratorPlugin>()));

            var result = codeGenerator.GenerateUseProvider("Data Source=Test", providerOptions: null);

            Assert.Equal("UseJet", result.Method);
            Assert.Collection(
                result.Arguments,
                a => Assert.Equal("Data Source=Test", a));
            Assert.Null(result.ChainedCall);
        }

        [Fact]
        public virtual void Use_provider_method_is_generated_correctly_with_options()
        {
            var codeGenerator = new JetCodeGenerator(
                new ProviderCodeGeneratorDependencies(
                    Enumerable.Empty<IProviderCodeGeneratorPlugin>()));

            var providerOptions = new MethodCallCodeFragment("SetProviderOption");

            var result = codeGenerator.GenerateUseProvider("Data Source=Test", providerOptions);

            Assert.Equal("UseJet", result.Method);
            Assert.Collection(
                result.Arguments,
                a => Assert.Equal("Data Source=Test", a),
                a =>
                {
                    var nestedClosure = Assert.IsType<NestedClosureCodeFragment>(a);

                    Assert.Equal("x", nestedClosure.Parameter);
                    Assert.Same(providerOptions, nestedClosure.MethodCall);
                });
            Assert.Null(result.ChainedCall);
        }
    }
}