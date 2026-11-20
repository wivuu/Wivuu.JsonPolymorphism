#if NET11_0_OR_GREATER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Wivuu.JsonPolymorphism;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Runs the generator over C# 15 source to check diagnostics and generated code for 'closed' types
    /// </summary>
    public class GeneratorTests
    {
        const string Header = """
            using System.Text.Json.Serialization;

            namespace Zoo;

            public enum AnimalKind { Dog, Cat }

            """;

        [Fact]
        public void TestClosedTypeCompiles()
        {
            // Example from https://github.com/wivuu/Wivuu.JsonPolymorphism/issues/19
            var result = Run(Header + """
                public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog() : Animal(AnimalKind.Dog);
                public record Cat() : Animal(AnimalKind.Cat);
                """);

            Assert.Empty(result.Diagnostics);

            // Modifiers come from the user's declaration only
            Assert.Contains(result.Converter("Animal").Split('\n'), line => line.Trim() == "partial record Animal");
        }

        [Fact]
        public void TestClosedTypeWriteIsExhaustive()
        {
            var result = Run(Header + """
                public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog() : Animal(AnimalKind.Dog);
                public record Cat() : Animal(AnimalKind.Cat);
                """);

            Assert.Empty(result.Diagnostics);
            Assert.DoesNotContain("default:", result.Converter("Animal"));
            Assert.DoesNotContain("is not a supported value", result.Converter("Animal"));
        }

        [Theory]
        [InlineData("closed", false)]
        // Another assembly could derive from an open abstract subtype, so the switch can't be exhaustive
        [InlineData("abstract", true)]
        public void TestClosedTypeIntermediateSubtype(string intermediateModifier, bool expectDefault)
        {
            var result = Run(Header + $$"""
                public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public {{intermediateModifier}} record Mammal(AnimalKind Kind) : Animal(Kind);
                public record Dog() : Mammal(AnimalKind.Dog);
                public record Cat() : Mammal(AnimalKind.Cat);
                """);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(expectDefault, result.Converter("Animal").Contains("default:"));
        }

        [Fact]
        public void TestClosedTypeWithFallback()
        {
            var result = Run(Header + """
                public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog() : Animal(AnimalKind.Dog);
                [JsonDiscriminatorFallback]
                public record Other(AnimalKind Kind) : Animal(Kind);
                """);

            Assert.Empty(result.Diagnostics);

            var converter = result.Converter("Animal");
            var write     = converter.Substring(converter.IndexOf("public override void Write"));

            Assert.Contains("case Other value2:", write);
            Assert.DoesNotContain("default:", write);
        }

        [Fact]
        public void TestClosedTypeUnmappedSubtype()
        {
            var result = Run(Header + """
                public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog() : Animal(AnimalKind.Dog);
                public record Cat() : Animal(AnimalKind.Cat);
                public record Lion() : Animal(AnimalKind.Cat);
                """);

            var diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("WIVUUJSONPOLY006", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("'Lion'", diagnostic.GetMessage());
            Assert.Contains("default:", result.Converter("Animal"));
        }

        [Fact]
        public void TestOpenTypeUnchanged()
        {
            // Unmapped subtypes of non-closed types are not reported, and Write keeps its default case
            var result = Run(Header + """
                public abstract partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog() : Animal(AnimalKind.Dog);
                public record Cat() : Animal(AnimalKind.Cat);
                public record Lion() : Animal(AnimalKind.Cat);
                """);

            Assert.Empty(result.Diagnostics);
            Assert.Contains("default:", result.Converter("Animal"));
        }

        [Fact]
        public void TestDiscriminatorNotFirst()
        {
            // Dog's own properties are written before its base type's
            var result = Run(Header + """
                public abstract partial record Animal([JsonDiscriminator] AnimalKind Kind);
                public record Dog(string Name) : Animal(AnimalKind.Dog);
                public record Cat() : Animal(AnimalKind.Cat);
                """);

            var diagnostic = Assert.Single(result.Diagnostics);

            Assert.Equal("WIVUUJSONPOLY007", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("'Dog' ('Name' is written before it)", diagnostic.GetMessage());
        }

        [Theory]
        // Ordered first explicitly
        [InlineData("""
            public abstract partial record Animal([property: JsonPropertyOrder(-1)][JsonDiscriminator] AnimalKind Kind);
            public record Dog(string Name) : Animal(AnimalKind.Dog);
            public record Cat() : Animal(AnimalKind.Cat);
            """)]
        // Nested discriminators may precede each other, but nothing else
        [InlineData("""
            public enum Breed { Poodle, Beagle }
            public abstract partial record Animal([property: JsonPropertyOrder(-1)][JsonDiscriminator] AnimalKind Kind);
            public abstract partial record Dog([property: JsonPropertyOrder(-1)][JsonDiscriminator] Breed Breed) : Animal(AnimalKind.Dog);
            public record Poodle(string Name) : Dog(Breed.Poodle);
            public record Beagle(string Name) : Dog(Breed.Beagle);
            public record Cat(string Name) : Animal(AnimalKind.Cat);
            """)]
        // A discriminator overridden by each subtype is declared by the most derived type, so it's written first
        [InlineData("""
            public abstract partial class Animal
            {
                [JsonDiscriminator] public virtual AnimalKind Kind { get; init; }
                public string Name { get; init; } = "";
            }
            public class Dog : Animal { public override AnimalKind Kind => AnimalKind.Dog; }
            public class Cat : Animal { public override AnimalKind Kind => AnimalKind.Cat; }
            """)]
        // Ignored and extension data properties aren't written before it
        [InlineData("""
            public abstract partial record Animal([JsonDiscriminator] AnimalKind Kind);
            public record Dog() : Animal(AnimalKind.Dog)
            {
                [JsonIgnore] public string? Secret { get; init; }
                [JsonExtensionData] public System.Collections.Generic.Dictionary<string, object>? Extra { get; set; }
            }
            public record Cat() : Animal(AnimalKind.Cat);
            """)]
        public void TestDiscriminatorFirst(string source)
        {
            var result = Run(Header + source);

            Assert.Empty(result.Diagnostics);
        }

        record GeneratorResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<SyntaxTree> GeneratedTrees)
        {
            public string Converter(string typeName) =>
                GeneratedTrees.Single(t => Path.GetFileName(t.FilePath) == $"{typeName}Converter.g.cs").ToString();
        }

        /// <summary>
        /// Runs the generator, returning generator diagnostics together with any warnings or errors in the resulting compilation
        /// </summary>
        static GeneratorResult Run(string source)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var runtimeDir   = Path.GetDirectoryName(typeof(object).Assembly.Location);

            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(path => Path.GetDirectoryName(path) == runtimeDir)
                .Select(path => MetadataReference.CreateFromFile(path));

            var compilation = CSharpCompilation.Create(
                "GeneratorTests",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            var driver = CSharpGeneratorDriver
                .Create(new ISourceGenerator[] { new JsonConverterGenerator() }, parseOptions: parseOptions)
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

            var diagnostics = generatorDiagnostics.AddRange(
                output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));

            return new GeneratorResult(diagnostics, driver.GetRunResult().GeneratedTrees);
        }
    }
}
#endif
