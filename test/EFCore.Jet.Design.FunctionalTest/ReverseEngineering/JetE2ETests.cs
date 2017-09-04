using System.Collections.Generic;
using System.Data.Jet;
using System.IO;
using System.Linq;
using EntityFramework.Jet.FunctionalTests;
using EntityFrameworkCore.Jet.Design.Internal;
using Microsoft.EntityFrameworkCore.ReverseEngineering;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests.ReverseEngineering
{
    public class JetE2ETests : E2ETestBase, IClassFixture<JetE2EFixture>
    {
        protected override string ProviderName => "EntityFrameworkCore.Jet.Design";

        protected override void ConfigureDesignTimeServices(IServiceCollection services)
            => new JetDesignTimeServices().ConfigureDesignTimeServices(services);

        public virtual string TestNamespace => "E2ETest.Namespace";
        public virtual string TestProjectDir => Path.Combine("E2ETest", "Output");
        public virtual string TestSubDir => "SubDir";
        public virtual string CustomizedTemplateDir => Path.Combine("E2ETest", "CustomizedTemplate", "Dir");

        //public static Microsoft.EntityFrameworkCore.Scaffolding.TableSelectionSet Filter
        //    => new Microsoft.EntityFrameworkCore.Scaffolding.TableSelectionSet(new List<string>{
        //        "AllDataTypes",
        //        "PropertyConfiguration",
        //        "Test Spaces Keywords Table",
        //        "MultipleFKsDependent",
        //        "MultipleFKsPrincipal",
        //        "OneToManyDependent",
        //        "OneToManyPrincipal",
        //        "OneToOneDependent",
        //        "OneToOnePrincipal",
        //        "OneToOneSeparateFKDependent",
        //        "OneToOneSeparateFKPrincipal",
        //        "OneToOneFKToUniqueKeyDependent",
        //        "OneToOneFKToUniqueKeyPrincipal",
        //        "ReferredToByTableWithUnmappablePrimaryKeyColumn",
        //        "TableWithUnmappablePrimaryKeyColumn",
        //        "selfReferencing",
        //    });

        public static IEnumerable<string> Tables
            => new List<string>
            {
                "AllDataTypes",
                "PropertyConfiguration",
                "Test Spaces Keywords Table",
                "MultipleFKsDependent",
                "MultipleFKsPrincipal",
                "OneToManyDependent",
                "OneToManyPrincipal",
                "OneToOneDependent",
                "OneToOnePrincipal",
                "OneToOneSeparateFKDependent",
                "OneToOneSeparateFKPrincipal",
                "OneToOneFKToUniqueKeyDependent",
                "OneToOneFKToUniqueKeyPrincipal",
                "ReferredToByTableWithUnmappablePrimaryKeyColumn",
                "TableWithUnmappablePrimaryKeyColumn",
                "selfreferencing"
            };

        // ReSharper disable once UnusedParameter.Local
        public JetE2ETests(JetE2EFixture fixture, ITestOutputHelper output)
            : base(output)
        {
        }

        private string _connectionString = JetConnection.GetConnectionString(@"E2E.accdb");

        private static readonly List<string> _expectedEntityTypeFiles = new List<string>
            {
                "AllDataTypes.expected",
                "MultipleFKsDependent.expected",
                "MultipleFKsPrincipal.expected",
                "OneToManyDependent.expected",
                "OneToManyPrincipal.expected",
                "OneToOneDependent.expected",
                "OneToOneFKToUniqueKeyDependent.expected",
                "OneToOneFKToUniqueKeyPrincipal.expected",
                "OneToOnePrincipal.expected",
                "OneToOneSeparateFKDependent.expected",
                "OneToOneSeparateFKPrincipal.expected",
                "PropertyConfiguration.expected",
                "ReferredToByTableWithUnmappablePrimaryKeyColumn.expected",
                "SelfReferencing.expected",
                "Test_Spaces_Keywords_Table.expected"
            };

        [Fact]
        [UseCulture("en-US")]
        public void E2ETestUseAttributesInsteadOfFluentApi()
        {
            var filePaths = Generator.Generate(
                    _connectionString,
                    Tables,
                    Enumerable.Empty<string>(),
                    TestProjectDir + Path.DirectorySeparatorChar, // tests that ending DirectorySeparatorChar does not affect namespace
                    TestSubDir,
                    TestNamespace,
                    contextName: "AttributesContext",
                    useDataAnnotations: true,
                    overwriteFiles: false,
                    useDatabaseNames: false);

            var actualFileSet = new FileSet(InMemoryFiles, Path.GetFullPath(Path.Combine(TestProjectDir, TestSubDir)))
            {
                Files = Enumerable.Repeat(filePaths.ContextFile, 1).Concat(filePaths.EntityTypeFiles).Select(Path.GetFileName).ToList()
            };

            var expectedFileSet = new FileSet(new FileSystemFileService(),
                Path.Combine("ReverseEngineering", "ExpectedResults", "E2E_UseAttributesInsteadOfFluentApi"),
                contents => contents.Replace("namespace " + TestNamespace, "namespace " + TestNamespace + "." + TestSubDir)
                    .Replace("{{connectionString}}", _connectionString))
            {
                Files = new List<string> { "AttributesContext.expected" }
                    .Concat(_expectedEntityTypeFiles).ToList()
            };

            AssertEqualFileContents(expectedFileSet, actualFileSet);
            //TODO ErikEJ Investigate compile issue
            //AssertCompile(actualFileSet);
        }

        [Fact]
        [UseCulture("en-US")]
        public void E2ETestAllFluentApi()
        {
            var filePaths = Generator.Generate(
                                _connectionString,
                                Tables,
                                Enumerable.Empty<string>(),
                                TestProjectDir,
                                outputPath: null, // not used for this test
                                rootNamespace: TestNamespace,
                                contextName: null,
                                useDataAnnotations: false,
                                overwriteFiles: false,
                                useDatabaseNames: false);

            var actualFileSet = new FileSet(InMemoryFiles, Path.GetFullPath(TestProjectDir))
            {
                Files = Enumerable.Repeat(filePaths.ContextFile, 1).Concat(filePaths.EntityTypeFiles).Select(Path.GetFileName).ToList()
            };

            var expectedFileSet = new FileSet(new FileSystemFileService(),
                Path.Combine("ReverseEngineering", "ExpectedResults", "E2E_AllFluentApi"),
                inputFile => inputFile.Replace("{{connectionString}}", _connectionString))
            {
                Files = new List<string> { "E2EContext.expected" }
                    .Concat(_expectedEntityTypeFiles).ToList()
            };

            AssertEqualFileContents(expectedFileSet, actualFileSet);
            //TODO ErikEJ Investigate compile issue
            //AssertCompile(actualFileSet);
        }

        [Fact]
        public void Non_null_boolean_columns_with_default_constraint_become_nullable_properties()
        {
            using (var scratch = JetTestStore.Create("NonNullBooleanWithDefaultConstraint"))
            {
                scratch.ExecuteNonQuery(@"
CREATE TABLE NonNullBoolWithDefault
(
     Id int NOT NULL PRIMARY KEY,
     BoolWithDefaultValueSql bit NOT NULL DEFAULT (CONVERT(""bit"", GETDATE())),
     BoolWithoutDefaultValueSql bit NOT NULL
)");

                var expectedFileSet = new FileSet(new FileSystemFileService(),
                    Path.Combine("ReverseEngineering", "ExpectedResults"),
                    contents => contents.Replace("{{connectionString}}", scratch.ConnectionString))
                {
                    Files = new List<string>
                    {
                        "NonNullBoolWithDefaultContext.cs",
                        "NonNullBoolWithDefault.cs",
                    }
                };

                var filePaths = Generator.Generate(
                        scratch.ConnectionString,
                        Enumerable.Empty<string>(),
                        Enumerable.Empty<string>(),
                        TestProjectDir + Path.DirectorySeparatorChar,
                        outputPath: null, // not used for this test
                        rootNamespace: TestNamespace,
                        contextName: "NonNullBoolWithDefaultContext",
                        useDataAnnotations: false,
                        overwriteFiles: false,
                        useDatabaseNames: false);


                var actualFileSet = new FileSet(InMemoryFiles, Path.GetFullPath(TestProjectDir))
                {
                    Files = new[] { filePaths.ContextFile }.Concat(filePaths.EntityTypeFiles).Select(Path.GetFileName).ToList()
                };

                AssertEqualFileContents(expectedFileSet, actualFileSet);
                //AssertCompile(actualFileSet);
            }
        }

        [ConditionalFact]
        public void Correct_arguments_to_scaffolding_typemapper()
        {
            using (var scratch = JetTestStore.Create("StringKeys"))
            {
                scratch.ExecuteNonQuery(@"
CREATE TABLE [StringKeysBlogs] (
    [PrimaryKey] nvarchar(256) NOT NULL,
    [AlternateKey] nvarchar(256) NOT NULL,
    [IndexProperty] nvarchar(256) NULL,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_StringKeysBlogs] PRIMARY KEY ([PrimaryKey]),
    CONSTRAINT [AK_StringKeysBlogs_AlternateKey] UNIQUE ([AlternateKey]));");
                scratch.ExecuteNonQuery(@"
CREATE INDEX [IX_StringKeysBlogs_IndexProperty] ON [StringKeysBlogs] ([IndexProperty]);");

                scratch.ExecuteNonQuery(@"
CREATE TABLE [StringKeysPosts] (
    [Id] int NOT NULL IDENTITY,
    [BlogAlternateKey] nvarchar(256) NULL,
    CONSTRAINT [PK_StringKeysPosts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StringKeysPosts_StringKeysBlogs_BlogAlternateKey] FOREIGN KEY ([BlogAlternateKey]) REFERENCES [StringKeysBlogs] ([AlternateKey]))");

                scratch.ExecuteNonQuery(@"
CREATE INDEX [IX_StringKeysPosts_BlogAlternateKey] ON [StringKeysPosts] ([BlogAlternateKey]);
");

                var expectedFileSet = new FileSet(new FileSystemFileService(),
                    Path.Combine("ReverseEngineering", "ExpectedResults"),
                    contents => contents.Replace("{{connectionString}}", scratch.ConnectionString))
                {
                    Files = new List<string>
                    {
                        "StringKeysContext.cs",
                        "StringKeysBlogs.cs",
                        "StringKeysPosts.cs",
                    }
                };

                var filePaths = Generator.Generate(
                    scratch.ConnectionString,
                    Enumerable.Empty<string>(),
                    Enumerable.Empty<string>(),
                    TestProjectDir + Path.DirectorySeparatorChar,
                    outputPath: null, // not used for this test
                    rootNamespace: TestNamespace,
                    contextName: "StringKeysContext",
                    useDataAnnotations: false,
                    overwriteFiles: false,
                    useDatabaseNames: false);

                var actualFileSet = new FileSet(InMemoryFiles, Path.GetFullPath(TestProjectDir))
                {
                    Files = new[] { filePaths.ContextFile }.Concat(filePaths.EntityTypeFiles).Select(Path.GetFileName).ToList()
                };

                AssertEqualFileContents(expectedFileSet, actualFileSet);
                //AssertCompile(actualFileSet);
            }
        }

        protected override ICollection<BuildReference> References { get; } = new List<BuildReference>
        {
            BuildReference.ByName("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
            BuildReference.ByName("System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
            BuildReference.ByName("System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"),
            BuildReference.ByName("System.ComponentModel.DataAnnotations, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"),
            BuildReference.ByName("EntityFrameworkCore.Jet"),
            BuildReference.ByName("System.Data.Jet"),
            
            BuildReference.ByName("Microsoft.EntityFrameworkCore"),
            BuildReference.ByName("Microsoft.EntityFrameworkCore.Relational"),
            BuildReference.ByName("Microsoft.Extensions.Caching.Abstractions"),
            BuildReference.ByName("Microsoft.Extensions.Logging.Abstractions")
        };
    }
}
