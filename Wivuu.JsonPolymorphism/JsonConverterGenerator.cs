using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Wivuu.JsonPolymorphism
{
    [Generator]
    class JsonConverterGenerator : ISourceGenerator
    {
        static object[] items = {

        };

        static readonly DiagnosticDescriptor DiagNotPartial = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY001",
            title: "Type with discriminator must be marked 'partial'",
            messageFormat: "Type '{0}' not marked 'Partial'",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor DiagNotEnum = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY002",
            title: "Type discriminator must be an enum",
            messageFormat: "Type '{0}' is not an enum",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor DiagNoCorrespondingType = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY003",
            title: "No type corresponding with enum member",
            messageFormat: "Member '{0}' has no corresponding type",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor DiagOnlyOneFallback = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY005",
            title: "Type with discriminator can only have a single JsonDiscriminatorFallback",
            messageFormat: "Type must have zero or one fallbacks",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor DiagClosedSubtypeNotMapped = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY006",
            title: "Subtype of a closed type has no corresponding discriminator value",
            messageFormat: "Type '{0}' derives from closed type '{1}' but no '{2}' member maps to it, so it cannot be serialized as '{1}'",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor DiagDiscriminatorNotFirst = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY007",
            title: "Discriminator is not the first property written",
            messageFormat: "Discriminator '{0}' is not the first property written for '{1}' ('{2}' is written before it), so deserializing '{3}' has to scan each object to find it. Add [JsonPropertyOrder(-1)] to '{0}', or [property: JsonPropertyOrder(-1)] on a record parameter.",
            category: "WivuuJsonPolymorphism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly string GeneratorAttributesText = new IndentedStringBuilder()
            .AppendLine("using System;")
            .AppendLine()
            .AppendLine("namespace System.Text.Json.Serialization")
            .Indent(    '{', sb => sb
                .AppendLine("[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]")
                .AppendLine("internal class JsonDiscriminatorAttribute : Attribute {")
                .AppendLine("    private string DiscriminatorName { get; }")
                .AppendLine("    public JsonDiscriminatorAttribute(string discriminatorName = null) {")
                .AppendLine("        DiscriminatorName = discriminatorName;")
                .AppendLine("    }")
                .AppendLine("}")
                .AppendLine()
                .AppendLine("[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]")
                .AppendLine("internal class JsonDiscriminatorFallbackAttribute : Attribute { }")
            )
            .ToString();

        static readonly string JsonInheritanceConverterText = """
            #nullable enable
            using System;
            using System.Text;
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace Wivuu.Polymorphism
            {
                internal abstract class JsonInheritanceConverter<T> : JsonConverter<T>
                {
                    private DiscriminatorPropertyNameCache? _propertyNameCache;

                    /// <summary>
                    /// Gets the name of the discriminator property.
                    /// </summary>
                    public abstract string DiscriminatorName { get; }

                    /// <summary>
                    /// Gets the CLR name of the discriminator property.
                    /// </summary>
                    protected abstract string DiscriminatorPropertyName { get; }

                    /// <summary>
                    /// Gets the name given to the discriminator property with <see cref="JsonPropertyNameAttribute"/>, if any.
                    /// </summary>
                    protected virtual string? DiscriminatorJsonPropertyName => null;

                    public override abstract T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
                    public override abstract void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options);

                    /// <summary>
                    /// Finds and deserializes the discriminator of the object at the reader's current position.
                    /// The reader is passed by value, so the caller's reader is not advanced and can still read the whole object.
                    /// </summary>
                    protected bool TryReadDiscriminator<TDiscriminator>(Utf8JsonReader reader, JsonSerializerOptions options, out TDiscriminator discriminator)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject)
                            throw new JsonException($"Unable to deserialize '{typeof(T).Name}': expected a JSON object but found '{reader.TokenType}'.");

                        var propertyName = GetDiscriminatorPropertyName(options);

                        // Only properties directly on this object (depth 1) are considered, nested values are skipped
                        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                        {
                            var isMatch = reader.ValueTextEquals(propertyName.Utf8Name) ||
                                (propertyName.IgnoreCase && string.Equals(reader.GetString(), propertyName.Name, StringComparison.OrdinalIgnoreCase));

                            reader.Read();

                            if (isMatch)
                            {
                                discriminator = JsonSerializer.Deserialize<TDiscriminator>(ref reader, options)!;
                                return true;
                            }

                            SkipValue(ref reader);
                        }

                        discriminator = default!;
                        return false;
                    }

                    /// <summary>
                    /// Skips the value at the reader's current position, leaving the reader on its last token.
                    /// </summary>
                    protected static void SkipValue(ref Utf8JsonReader reader)
                    {
                        // TrySkip rather than Skip: when deserializing from a stream the reader is not on its final block,
                        // but System.Text.Json buffers the entire value before calling a custom converter, so this succeeds
                        if (!reader.TrySkip())
                            throw new JsonException("Unexpected end of JSON data.");
                    }

                    private DiscriminatorPropertyNameCache GetDiscriminatorPropertyName(JsonSerializerOptions options)
                    {
                        var cache = _propertyNameCache;

                        if (cache is null || !ReferenceEquals(cache.Options, options))
                        {
                            // Match the name System.Text.Json writes: an explicit [JsonPropertyName] wins, otherwise apply the naming policy
                            var name = DiscriminatorJsonPropertyName
                                ?? options.PropertyNamingPolicy?.ConvertName(DiscriminatorPropertyName)
                                ?? DiscriminatorPropertyName;

                            _propertyNameCache = cache = new DiscriminatorPropertyNameCache(options, name);
                        }

                        return cache;
                    }

                    private sealed class DiscriminatorPropertyNameCache
                    {
                        public DiscriminatorPropertyNameCache(JsonSerializerOptions options, string name)
                        {
                            Options    = options;
                            Name       = name;
                            Utf8Name   = Encoding.UTF8.GetBytes(name);
                            IgnoreCase = options.PropertyNameCaseInsensitive;
                        }

                        public JsonSerializerOptions Options { get; }
                        public string Name { get; }
                        public byte[] Utf8Name { get; }
                        public bool IgnoreCase { get; }
                    }
                }
            }
            """;

        public void Initialize(GeneratorInitializationContext context)
        {
#if DEBUG
            System.Diagnostics.Debugger.Launch();
#endif
            context.RegisterForSyntaxNotifications(() => new JsonDiscriminatorReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var jsonAttributeSource = SourceText.From(GeneratorAttributesText, Encoding.UTF8);
            context.AddSource("JsonDiscriminatorAttributes.g.cs", jsonAttributeSource);

            // Retreive the populated receiver 
            if (context.SyntaxReceiver is not JsonDiscriminatorReceiver receiver ||
                receiver.Candidates.Count == 0)
                return;

            // Retrieve CSharp compilation from context
            if (context.Compilation is not CSharpCompilation prevCompilation)
                return;

            // Add new attribute to compilation
            var options     = prevCompilation.SyntaxTrees[0].Options as CSharpParseOptions;
            var compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(jsonAttributeSource, options));

            // Retrieve enum type
            var enumType = compilation.GetTypeByMetadataName(typeof(System.Enum).FullName!);

            // Retrieve STJ attributes which affect the name and order of the discriminator property
            var jsonAttributes = new JsonAttributeTypes(
                PropertyName:  compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonPropertyNameAttribute"),
                PropertyOrder: compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonPropertyOrderAttribute"),
                Ignore:        compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIgnoreAttribute"),
                Include:       compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonIncludeAttribute"),
                ExtensionData: compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonExtensionDataAttribute"));

            // Define shared base class for all generated converters. The name 'JsonInheritanceConverter' and the
            // 'DiscriminatorName' property are what NJsonSchema / NSwag look for when generating schemas.
            context.AddSource($"JsonInheritanceConverter.g.cs", SourceText.From(JsonInheritanceConverterText, Encoding.UTF8));

            var discriminators = new List<(AttributeData attr, CSharpSyntaxNode node, ISymbol symbol)>(receiver.GetDiscriminators(compilation));

            // CLR name of the discriminator property declared by each type
            var discriminatorNames = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);

            foreach (var (_, _, discriminator) in discriminators)
                if (discriminator.ContainingType is INamedTypeSymbol declaringType)
                    discriminatorNames[declaringType] = discriminator.Name;

            // Create JsonConverters
            foreach (var (attr, node, symbol) in discriminators)
            {
                if (GetParentDeclaration(node) is not TypeDeclarationSyntax parentTypeNode)
                    continue;

                if (symbol.ContainingType is not INamedTypeSymbol parentSymbol)
                    continue;

                // Ensure that parent type is partial so we can attach the JsonConverter attribute
                if (!parentTypeNode.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagNotPartial, parentSymbol.Locations[0], parentSymbol.Name));
                    continue;
                }

                // Ensure that the discriminator is an enum
                var discriminatorType =
                    symbol is IParameterSymbol param ? param.Type as INamedTypeSymbol :
                    symbol is IPropertySymbol prop   ? prop.Type as INamedTypeSymbol  :
                    default;

                if (discriminatorType is null ||
                    discriminatorType.BaseType?.Equals(enumType, SymbolEqualityComparer.Default) is not true)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagNotEnum, symbol.Locations[0], discriminatorType?.Name ?? symbol.Name));
                    continue;
                }

                // Retrieve fallback attributes for the parent type
                var fallbacks = new List<(CSharpSyntaxNode, ISymbol)>(receiver.GetFallbacks(compilation, parentSymbol));

                if (fallbacks.Count > 1)
                {
                    foreach (var (_, fbSymbol) in fallbacks)
                        context.ReportDiagnostic(
                            Diagnostic.Create(DiagOnlyOneFallback, fbSymbol.Locations[0]));
                    continue;
                }

                // Get all possible enum values and corresponding types
                var classMembers = new List<(string, INamedTypeSymbol, int)>(
                    GetCorrespondingTypes(context, compilation, parentSymbol, discriminatorType, fallbacks.Count == 1));

                classMembers.Sort(new SpecificityComparer());

                var fallbackSymbol = fallbacks.Count == 1 ? fallbacks[0].Item2 as INamedTypeSymbol : null;

                // Types handled by the generated Write method
                var handledTypes = new List<INamedTypeSymbol>();

                foreach (var (_, type, _) in classMembers)
                    handledTypes.Add(type);

                if (fallbackSymbol is not null)
                    handledTypes.Add(fallbackSymbol);

                // C# 15 'closed' types can only be derived from in the same assembly, so every subtype can be
                // checked here instead of failing at runtime, and the Write switch can drop its default case
                // when it provably handles every possible value
                var isExhaustive = false;
                var subtypes     = GetSubtypes(compilation, parentSymbol);

                if (IsClosed(parentSymbol))
                {
                    foreach (var subtype in subtypes)
                    {
                        if (subtype.IsAbstract || IsClosed(subtype) || IsHandled(subtype, handledTypes))
                            continue;

                        context.ReportDiagnostic(
                            Diagnostic.Create(DiagClosedSubtypeNotMapped, subtype.Locations[0], subtype.Name, parentSymbol.Name, discriminatorType.Name));
                    }

                    isExhaustive = classMembers.Count > 0 && IsExhaustive(parentSymbol, subtypes, handledTypes);
                }

                // Reading finds the discriminator immediately when it's the first property, so warn (once) when
                // any concrete type in the hierarchy writes another property before it
                if (jsonAttributes.PropertyOrder is not null)
                {
                    var concreteTypes = new List<INamedTypeSymbol>();

                    if (!parentSymbol.IsAbstract && !IsClosed(parentSymbol))
                        concreteTypes.Add(parentSymbol);

                    foreach (var subtype in subtypes)
                        if (!subtype.IsAbstract && !IsClosed(subtype))
                            concreteTypes.Add(subtype);

                    foreach (var concreteType in concreteTypes)
                    {
                        if (GetPropertyWrittenBefore(concreteType, symbol.Name, discriminatorNames, jsonAttributes) is not string writtenBefore)
                            continue;

                        context.ReportDiagnostic(
                            Diagnostic.Create(DiagDiscriminatorNotFirst, symbol.Locations[0], symbol.Name, concreteType.Name, writtenBefore, parentSymbol.Name));
                        break;
                    }
                }

                var jsonPropertyName = GetJsonPropertyName(symbol, parentSymbol, jsonAttributes.PropertyName);

                var sb = new IndentedStringBuilder();

                sb.AppendLine("#nullable enable")
                  .AppendLine("using System;")
                  .AppendLine("using System.Runtime.Serialization;")
                  .AppendLine("using System.Text.Json;")
                  .AppendLine("using System.Text.Json.Serialization;")
                  .AppendLine()
                  ;

                var ns = symbol.ContainingType.ContainingNamespace;

                // Types in the global namespace (e.g. alongside top-level statements) get no namespace block
                using (ns.IsGlobalNamespace ? null : sb.AppendLine($"namespace {ns.ToDisplayString()}").Indent('{'))
                {
                    // Append known types
                    foreach (var (_,className,_) in classMembers)
                        sb.AppendLine($"[KnownType(typeof({className.Name}))]");

                    // Attribute adding class
                    sb.AppendLine($"[JsonConverter(typeof({parentSymbol.Name}Converter))]");

                    // Only 'partial' is emitted; accessibility, 'abstract', 'closed' etc. already come from the user's
                    // declarations and don't need to be repeated, which keeps the generated part valid for any of them
                    using (sb.AppendLine($"partial {parentTypeNode.Keyword.Text} {parentSymbol.Name}").Indent('{'))
                    {
                        // Add static array to house all types
                        using (sb.AppendLine($"private static readonly Type[] _allTypes = ").Indent('{', endCh: "};"))
                        {
                            // Iterate through each member case
                            foreach (var (_, type, _) in classMembers)
                                sb.AppendLine($"typeof({type}),");
                        }

                        // Add static method to get all types
                        sb.AppendLine($"/// <summary>")
                          .AppendLine($"/// Gets all types that are known to the <see cref=\"{parentSymbol.Name}\"/> converter.")
                          .AppendLine($"/// </summary>")
                          .AppendLine($"/// <returns>All types that are known to the <see cref=\"{parentSymbol.Name}\"/> converter.</returns>")
                          .AppendLine("#pragma warning disable CS0109");
                        using (sb.AppendLine($"public static new Type[] GetAllTypes()").Indent('{'))
                        {
                            sb.AppendLine("return _allTypes;");
                        }
                        sb.AppendLine("#pragma warning restore CS0109");

                        // Add static method to get type from discriminator
                        using (sb.AppendLine($"public static Type? GetType({discriminatorType} kind)").Indent('{'))
                        {
                            using (sb.AppendLine($"return kind switch").Indent('{', endCh: "};"))
                            {
                                // Iterate through each member case
                                foreach (var (member, type, _) in classMembers)
                                    sb.AppendLine($"{discriminatorType}.{member} => typeof({type}),");

                                if (fallbacks.Count == 1 && fallbacks[0] is var (_, fbSymbol))
                                    sb.AppendLine($"_ => typeof({fbSymbol.Name})");
                                else
                                    sb.AppendLine("_ => default");
                            }
                        }
                    }

                    // Converter class
                    using (sb.AppendLine($"internal class {parentSymbol.Name}Converter : Wivuu.Polymorphism.JsonInheritanceConverter<{parentSymbol.Name}>").Indent('{'))
                    {
                        var renderedName = attr?.ConstructorArguments.Length is > 0 
                            ? attr.ConstructorArguments[0].Value?.ToString() 
                            : null;

                        // DiscriminatorName property
                        sb.AppendLine($"/// <summary>")
                          .AppendLine($"/// Gets the name of the discriminator property.")
                          .AppendLine($"/// </summary>")
                          .AppendLine($"public override string DiscriminatorName => {Literal(renderedName ?? jsonPropertyName ?? symbol.MetadataName)};")
                          .AppendLine()
                          .AppendLine($"protected override string DiscriminatorPropertyName => {Literal(symbol.MetadataName)};")
                          .AppendLine()
                          ;

                        if (jsonPropertyName is not null)
                            sb.AppendLine($"protected override string? DiscriminatorJsonPropertyName => {Literal(jsonPropertyName)};")
                              .AppendLine()
                              ;

                        // Read Method
                        using (sb.AppendLine($"public override {parentSymbol.Name}? Read").Indent('('))
                            sb.AppendLine("ref Utf8JsonReader reader,")
                              .AppendLine("Type typeToConvert,")
                              .AppendLine("JsonSerializerOptions options")
                              ;

                        using (sb.Indent('{'))
                        {
                            // Find the discriminator on a copy of the reader, then deserialize the subtype directly
                            // from the original reader; no intermediate JsonElement is allocated or parsed
                            using (sb.AppendLine($"if (TryReadDiscriminator<{discriminatorType}>(reader, options, out var discriminator))").Indent('{'))
                            using (sb.AppendLine("switch (discriminator)").Indent('{'))
                            {
                                // Iterate through each member case
                                foreach (var (member, type, _) in classMembers)
                                {
                                    using (sb.AppendLine($"case {discriminatorType}.{member}:").Indent())
                                        sb.AppendLine($"return JsonSerializer.Deserialize<{type}>(ref reader, options);");

                                    sb.AppendLine();
                                }

                                if (fallbackSymbol is not null)
                                {
                                    using (sb.AppendLine("default:").Indent())
                                        sb.AppendLine($"return JsonSerializer.Deserialize<{fallbackSymbol.Name}>(ref reader, options);");
                                }
                            }

                            // Missing or unknown discriminator, consume the object so the reader is left at its end
                            sb.AppendLine()
                              .AppendLine("SkipValue(ref reader);")
                              .AppendLine("return default;");
                        }

                        sb.AppendLine();

                        // Write Method
                        using (sb.AppendLine($"public override void Write").Indent('('))
                            sb.AppendLine("Utf8JsonWriter writer,")
                              .AppendLine($"{parentSymbol.Name} value,")
                              .AppendLine("JsonSerializerOptions options")
                              ;

                        using (sb.Indent('{'))
                        using (sb.AppendLine("switch (value)").Indent('{'))
                        {
                            var i = 0;

                            // Iterate through each member case
                            foreach (var (_, className, _) in classMembers)
                            {
                                ++i;

                                using (sb.AppendLine($"case {className.Name} value{i}:").Indent())
                                    sb.AppendLine($"JsonSerializer.Serialize(writer, value{i}, options);")
                                      .AppendLine("break;")
                                      ;

                                sb.AppendLine();
                            }

                            if (isExhaustive)
                            {
                                // The fallback gets its own case, unless a more specific case already handles it
                                // (a duplicate case would be a compile error)
                                if (fallbackSymbol is not null && !IsHandled(fallbackSymbol, handledTypes, exclude: fallbackSymbol))
                                {
                                    ++i;

                                    using (sb.AppendLine($"case {fallbackSymbol.Name} value{i}:").Indent())
                                        sb.AppendLine($"JsonSerializer.Serialize(writer, value{i}, options);")
                                          .AppendLine("break;")
                                          ;

                                    sb.AppendLine();
                                }

                                sb.AppendLine($"// '{parentSymbol.Name}' is closed and every subtype is handled above, so no default case is needed");
                            }
                            else using (sb.AppendLine($"default:").Indent())
                                if (fallbacks.Count == 1 && fallbacks[0] is var (_, fbSymbol))
                                {
                                    ++i;

                                    sb.AppendLine($"if (value is {fbSymbol.Name} value{i})").Indent('{', sb =>
                                        sb.AppendLine($"JsonSerializer.Serialize(writer, value{i}, options);")
                                          .AppendLine("break;")
                                       )
                                      .AppendLine("else")
                                      .AppendLine($"    throw new JsonException($\"{{value.{symbol.MetadataName}}} is not a supported value\");");
                                      ;
                                }
                                else
                                    sb.AppendLine($"throw new JsonException($\"{{value.{symbol.MetadataName}}} is not a supported value\");");
                        }
                    }
                }

                // Add source
                context.AddSource($"{parentSymbol.Name}Converter.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        }

        static IEnumerable<(string name, INamedTypeSymbol match, int level)> GetCorrespondingTypes(
            GeneratorExecutionContext context, 
            Compilation compilation,
            INamedTypeSymbol parentSymbol,
            INamedTypeSymbol discriminatorEnum,
            bool hasFallback)
        {
            var used = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var member in discriminatorEnum.GetMembers())
            {
                if (member is not IFieldSymbol field)
                    continue;

                if (field.ConstantValue is null)
                    continue;

                var name = member.Name;

                // Get matching types in the context
                var matches = compilation
                    .GetSymbolsWithName(
                        p => p.Contains(name),
                        SymbolFilter.Type
                    );

                var any = false;

                // Ensure type inherits from node
                foreach (INamedTypeSymbol match in matches)
                {
                    var level = GetIsBaseTypeAll(match, parentSymbol);

                    if (level > 0)
                    {
                        // Only allow symbol to match once
                        if (!used.Add(match))
                            continue;

                        yield return (name, match, level);
                        any = true;
                        break;
                    }
                }

                if (!any && !hasFallback)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagNoCorrespondingType, discriminatorEnum.Locations[0], name));
                }
            }
        }

        static string Literal(string value) =>
            SymbolDisplay.FormatLiteral(value, quote: true);

        /// <summary>
        /// Whether the type is declared with the C# 15 'closed' modifier. Checked syntactically so this works
        /// regardless of the Roslyn version the generator is compiled against.
        /// </summary>
        internal static bool IsClosed(INamedTypeSymbol type)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax declaration)
                    continue;

                foreach (var modifier in declaration.Modifiers)
                    if (modifier.ValueText == "closed")
                        return true;
            }

            return false;
        }

        /// <summary>
        /// All types in the compilation which derive, directly or indirectly, from <paramref name="parent"/>
        /// </summary>
        static List<INamedTypeSymbol> GetSubtypes(Compilation compilation, INamedTypeSymbol parent)
        {
            var subtypes = new List<INamedTypeSymbol>();
            var seen     = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var symbol in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol type && GetIsBaseTypeAll(type, parent) > 0 && seen.Add(type))
                    subtypes.Add(type);
            }

            return subtypes;
        }

        /// <summary>
        /// Whether a switch case for one of <paramref name="handledTypes"/> matches instances of <paramref name="type"/>
        /// </summary>
        static bool IsHandled(INamedTypeSymbol type, List<INamedTypeSymbol> handledTypes, INamedTypeSymbol? exclude = null)
        {
            foreach (var handled in handledTypes)
            {
                if (exclude is not null && handled.Equals(exclude, SymbolEqualityComparer.Default))
                    continue;

                if (GetIsBaseTypeAll(type, handled) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether every possible instance of the closed type <paramref name="closedType"/> is handled. Only closed types
        /// can be proven exhaustive: any other unhandled abstract type could be derived from in another assembly.
        /// </summary>
        static bool IsExhaustive(INamedTypeSymbol closedType, List<INamedTypeSymbol> subtypes, List<INamedTypeSymbol> handledTypes)
        {
            foreach (var subtype in subtypes)
            {
                // Only direct subtypes; indirect ones are covered by recursing into closed subtypes
                if (subtype.BaseType?.Equals(closedType, SymbolEqualityComparer.Default) is not true)
                    continue;

                if (IsHandled(subtype, handledTypes))
                    continue;

                if (IsClosed(subtype) && IsExhaustive(subtype, subtypes, handledTypes))
                    continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the name given to the discriminator with [JsonPropertyName], which for a record parameter
        /// is applied to the generated property with [property: JsonPropertyName(...)]
        /// </summary>
        static string? GetJsonPropertyName(ISymbol discriminator, INamedTypeSymbol parentSymbol, INamedTypeSymbol? jsonPropertyNameType)
        {
            if (jsonPropertyNameType is null)
                return null;

            var property = discriminator as IPropertySymbol;

            if (discriminator is IParameterSymbol parameter)
            {
                foreach (var member in parentSymbol.GetMembers(parameter.Name))
                {
                    if (member is IPropertySymbol parameterProperty)
                    {
                        property = parameterProperty;
                        break;
                    }
                }
            }

            if (property is null)
                return null;

            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.Equals(jsonPropertyNameType, SymbolEqualityComparer.Default) is true &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string name)
                    return name;
            }

            return null;
        }

        /// <summary>
        /// Follows the order System.Text.Json writes an object's properties in: those declared by the most derived type
        /// first, then each base type's (properties before fields), stably sorted by [JsonPropertyOrder]. Returns the first
        /// property written before the discriminator, ignoring other discriminators in the hierarchy since they can't all
        /// be first, or null if the discriminator comes first.
        /// </summary>
        static string? GetPropertyWrittenBefore(
            INamedTypeSymbol type,
            string discriminatorName,
            Dictionary<INamedTypeSymbol, string> discriminatorNames,
            JsonAttributeTypes json)
        {
            var written                 = new List<(string name, int order)>();
            var seen                    = new HashSet<string>();
            var hierarchyDiscriminators = new HashSet<string>();

            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (discriminatorNames.TryGetValue(current, out var name))
                    hierarchyDiscriminators.Add(name);

                // A property declared by a derived type (e.g. an override) takes the place of the base type's
                foreach (var member in current.GetMembers())
                {
                    if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false } property || !seen.Add(property.Name))
                        continue;

                    var getter   = property.GetMethod ?? property.OverriddenProperty?.GetMethod;
                    var isPublic = property.DeclaredAccessibility == Accessibility.Public &&
                                   getter?.DeclaredAccessibility == Accessibility.Public;

                    if (getter is not null && (isPublic || HasAttribute(property, json.Include)) && IsWritten(property, json))
                        written.Add((property.Name, GetPropertyOrder(property, json)));
                }

                foreach (var member in current.GetMembers())
                {
                    if (member is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field &&
                        HasAttribute(field, json.Include) && seen.Add(field.Name) && IsWritten(field, json))
                        written.Add((field.Name, GetPropertyOrder(field, json)));
                }
            }

            foreach (var (name, _) in written.OrderBy(p => p.order))
            {
                if (name == discriminatorName)
                    return null;

                if (!hierarchyDiscriminators.Contains(name))
                    return name;
            }

            // Discriminator isn't written at all
            return null;
        }

        static bool IsWritten(ISymbol member, JsonAttributeTypes json)
        {
            foreach (var attribute in member.GetAttributes())
            {
                // Extension data is written after all other properties
                if (attribute.AttributeClass?.Equals(json.ExtensionData, SymbolEqualityComparer.Default) is true)
                    return false;

                if (attribute.AttributeClass?.Equals(json.Ignore, SymbolEqualityComparer.Default) is true)
                {
                    // Condition defaults to JsonIgnoreCondition.Always (1); conditional ignores may still write the property
                    var condition = 1;

                    foreach (var argument in attribute.NamedArguments)
                        if (argument.Key == "Condition" && argument.Value.Value is int value)
                            condition = value;

                    if (condition == 1)
                        return false;
                }
            }

            return true;
        }

        static int GetPropertyOrder(ISymbol member, JsonAttributeTypes json)
        {
            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.Equals(json.PropertyOrder, SymbolEqualityComparer.Default) is true &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is int order)
                    return order;
            }

            return 0;
        }

        static bool HasAttribute(ISymbol member, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
                return false;

            foreach (var attribute in member.GetAttributes())
                if (attribute.AttributeClass?.Equals(attributeType, SymbolEqualityComparer.Default) is true)
                    return true;

            return false;
        }

        internal record JsonAttributeTypes(
            INamedTypeSymbol? PropertyName,
            INamedTypeSymbol? PropertyOrder,
            INamedTypeSymbol? Ignore,
            INamedTypeSymbol? Include,
            INamedTypeSymbol? ExtensionData);

        internal static int GetIsBaseTypeAll(INamedTypeSymbol match, INamedTypeSymbol of, int level = 0) =>
            match switch
            {
                // TODO: Only concrete types considered
                var m when m.Equals(of, SymbolEqualityComparer.Default) => level,
                var m when m.BaseType is INamedTypeSymbol ty => GetIsBaseTypeAll(ty, of, level + 1),
                _ => -1,
            };

        internal static TypeDeclarationSyntax? GetParentDeclaration(SyntaxNode node) => 
            node.Parent switch
            {
                TypeDeclarationSyntax type => type,
                not null => GetParentDeclaration(node.Parent),
                _ => null
            };

        internal class SpecificityComparer : IComparer<(string, INamedTypeSymbol, int level)>
        {
            public int Compare(
                (string, INamedTypeSymbol, int level) x,
                (string, INamedTypeSymbol, int level) y) => 
                y.level.CompareTo(x.level);
        }
    }

    class JsonDiscriminatorReceiver : ISyntaxReceiver
    {
        public List<CSharpSyntaxNode> Candidates { get; } = new();
        public List<CSharpSyntaxNode> Fallbacks { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is PropertyDeclarationSyntax propDecl)
            {
                if (propDecl.AttributeLists.Count != 0)
                    Candidates.Add(propDecl);
            }
            else if (syntaxNode is ParameterSyntax paramDecl)
            {
                if (paramDecl.AttributeLists.Count != 0)
                    Candidates.Add(paramDecl);
            }
            else if (syntaxNode is TypeDeclarationSyntax typeDecl)
            {
                if (typeDecl.AttributeLists.Count != 0)
                    Fallbacks.Add(typeDecl);
            }
        }

        /// <summary>
        /// Retrieve all the discriminators from the execution context
        /// </summary>
        public IEnumerable<(AttributeData attr, CSharpSyntaxNode node, ISymbol symbol)> GetDiscriminators(Compilation compilation)
        {
            var attributeSymbol = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDiscriminatorAttribute");

            // Find all discriminators
            for (var i = 0; i < Candidates.Count; ++i)
            {
                var node  = Candidates[i];
                var model = compilation.GetSemanticModel(node.SyntaxTree);

                if (model.GetDeclaredSymbol(node) is not ISymbol symbol)
                    continue;

                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) is true)
                        yield return (attr, node, symbol);
                }
            }
        }

        /// <summary>
        /// Retrieve all the fallbacks from the execution context
        /// </summary>
        public IEnumerable<(CSharpSyntaxNode node, ISymbol symbol)> GetFallbacks(Compilation compilation, INamedTypeSymbol parentSymbol)
        {
            var attributeSymbol = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDiscriminatorFallbackAttribute");

            // Find all fallbacks
            for (var i = 0; i < Fallbacks.Count; ++i)
            {
                var node  = Fallbacks[i];
                var model = compilation.GetSemanticModel(node.SyntaxTree);

                if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol)
                    continue;

                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) is true)
                    {
                        var level = JsonConverterGenerator.GetIsBaseTypeAll(symbol, parentSymbol);

                        if (level == 0)
                            // TODO: Ensure type is concrete
                            yield return (node, symbol);

                        else if (level > 0)
                            yield return (node, symbol);
                    }
                }
            }
        }
    }
}
