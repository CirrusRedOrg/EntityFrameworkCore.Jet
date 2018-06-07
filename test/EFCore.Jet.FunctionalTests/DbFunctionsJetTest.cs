// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace EntityFramework.Jet.FunctionalTests
{
    public class DbFunctionsJetTest : DbFunctionsTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public DbFunctionsJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Like_literal()
        {
            base.Like_literal();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE N'%M%'");
        }

        public override void Like_identity()
        {
            base.Like_identity();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE [c].[ContactName]");
        }

        public override void Like_literal_with_escape()
        {
            base.Like_literal_with_escape();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[ContactName] LIKE N'!%' ESCAPE N'!'");
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public async void FreeText_literal()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_client_eval_throws()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_multiple_words()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_with_language_term()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_with_multiple_words_and_language_term()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_multiple_predicates()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_throws_for_no_FullText_index()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public void FreeText_through_navigation()
        {
        }

        public void FreeText_through_navigation_with_language_terms()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public async void FreeText_throws_when_using_non_parameter_or_constant_for_freetext_string()
        {
        }

        [Fact(Skip = "FullTextSearch unsupported")]
        public async void FreeText_throws_when_using_non_column_for_proeprty_reference()
        {
        }


        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertSql(expected);

        private void AssertContains(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertContains(expected);

    }
}
