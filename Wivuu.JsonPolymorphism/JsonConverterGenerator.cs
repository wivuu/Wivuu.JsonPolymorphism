using System.Collections.Generic;
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

        static readonly DiagnosticDescriptor DiagTypeNotBeConcrete = new DiagnosticDescriptor(
            id: "WIVUUJSONPOLY004",
            title: "Type with discriminator must be abstract or interface",
            messageFormat: "Type must not be concrete",
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

        static readonly string GeneratorAttributesText = new IndentedStringBuilder()
            .AppendLine("using System;")
            .AppendLine()
            .AppendLine("namespace System.Text.Json.Serialization")
            .Indent(    '{', sb => sb
                .AppendLine("[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]")
                .AppendLine("public class JsonDiscriminatorAttribute : Attribute { }")
                .AppendLine()
                .AppendLine("[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]")
                .AppendLine("public class JsonDiscriminatorFallbackAttribute : Attribute { }")
            )
            .ToString();

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
            var enumType = compilation.GetTypeByMetadataName(typeof(System.Enum).FullName);

            // Create JsonConverters
            foreach (var (node, symbol) in receiver.GetDiscriminators(compilation))
            {
                if (GetParentDeclaration(node) is not TypeDeclarationSyntax parentTypeNode)
                    continue;

                if (symbol.ContainingType is not INamedTypeSymbol parentSymbol)
                    continue;

                // Ensure that parent type is not concrete so that we do not cause stack overflow on serialize
                if (!parentTypeNode.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
                     parentSymbol.TypeKind != TypeKind.Interface)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DiagTypeNotBeConcrete, parentSymbol.Locations[0]));
                    continue;
                }

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

                var sb = new IndentedStringBuilder();

                sb.AppendLine("#nullable enable")
                  .AppendLine("using System;")
                  .AppendLine("using System.Runtime.Serialization;")
                  .AppendLine("using System.Text.Json;")
                  .AppendLine("using System.Text.Json.Serialization;")
                  .AppendLine()
                  ;

                var ns = symbol.ContainingType.ContainingNamespace;

                using (sb.AppendLine($"namespace {ns.ToDisplayString()}").Indent('{'))
                {
                    // Append known types
                    foreach (var (_,className,_) in classMembers)
                        sb.AppendLine($"[KnownType(typeof({className.Name}))]");

                    // Attribute adding class
                    sb.AppendLine($"[JsonConverter(typeof({parentSymbol.Name}Converter))]")
                      .AppendLine($"{parentTypeNode.Modifiers} {parentTypeNode.Keyword.Text} {parentSymbol.Name} {{ }}")
                      .AppendLine()
                      ;

                    // Converter class
                    var visibility =
                        parentTypeNode.Modifiers.Any(SyntaxKind.InternalKeyword)  ? "internal "  :
                        parentTypeNode.Modifiers.Any(SyntaxKind.ProtectedKeyword) ? "protected " :
                        parentTypeNode.Modifiers.Any(SyntaxKind.PrivateKeyword)   ? "private "   :
                        parentTypeNode.Modifiers.Any(SyntaxKind.PublicKeyword)    ? "public "    :
                        "";

                    using (sb.AppendLine($"{visibility}class {parentSymbol.Name}Converter : JsonConverter<{parentSymbol.Name}>").Indent('{'))
                    {
                        // Read Method
                        using (sb.AppendLine($"public override {parentSymbol.Name}? Read").Indent('('))
                            sb.AppendLine("ref Utf8JsonReader reader,")
                              .AppendLine("Type typeToConvert,")
                              .AppendLine("JsonSerializerOptions options")
                              ;

                        using (sb.Indent('{'))
                        {
                            // TODO: Determine better way to find in case insensitive way
                            var ident      = symbol.MetadataName;
                            var camelIdent = string.Concat(char.ToLowerInvariant(ident[0]), ident.Substring(1));

                            sb.AppendLine("var deserializedObj = JsonSerializer.Deserialize<JsonElement>(ref reader, options);")
                              .AppendLine("var discriminator   = options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase")
                              .AppendLine($"    ? \"{camelIdent}\" : \"{ident}\";")
                              .AppendLine()
                              ;

                            using (sb.AppendLine("if (deserializedObj.TryGetProperty(discriminator, out var property))").Indent('{'))
                            using (sb.AppendLine("return property.ValueKind").Indent())
                            {
                                using (sb.AppendLine("switch").Indent('{'))
                                    sb.AppendLine($"JsonValueKind.String => Enum.TryParse<{discriminatorType}>(property.GetString(), out var stringKind) ?")
                                      .AppendLine($"    stringKind : throw new JsonException($\"Cant convert value {{property.GetRawText()}} to {discriminatorType.Name}\"),")
                                      .AppendLine()
                                      .AppendLine($"JsonValueKind.Number => ({discriminatorType})property.GetInt32(),")
                                      .AppendLine()
                                      .AppendLine($"_ => throw new JsonException($\"Cant convert value {{property.GetRawText()}} to {discriminatorType.Name}\")")
                                      ;

                                using (sb.AppendLine("switch").Indent('{', endCh: "};"))
                                {
                                    // Iterate through each member case
                                    foreach (var (member, type, _) in classMembers)
                                        sb.AppendLine($"{discriminatorType}.{member} => JsonSerializer.Deserialize<{type}>(")
                                          .AppendLine("    deserializedObj.GetRawText(), options")
                                          .AppendLine("),")
                                          .AppendLine()
                                          ;

                                    if (fallbacks.Count == 1 && fallbacks[0] is var (_, fbSymbol))
                                    {
                                        sb.AppendLine($"_ => JsonSerializer.Deserialize<{fbSymbol.Name}>(")
                                          .AppendLine("    deserializedObj.GetRawText(), options")
                                          .AppendLine("),")
                                          .AppendLine()
                                          ;
                                    }
                                    else
                                        sb.AppendLine("_ => default");
                                }
                            }

                            sb.AppendLine("return default;");
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

                            using (sb.AppendLine($"default:").Indent())
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

            foreach (var name in discriminatorEnum.MemberNames)
            {
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

        internal static int GetIsBaseTypeAll(INamedTypeSymbol match, INamedTypeSymbol of, int level = 0) =>
            match switch
            {
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
        public IEnumerable<(CSharpSyntaxNode node, ISymbol symbol)> GetDiscriminators(Compilation compilation)
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
                        yield return (node, symbol);
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

                        if (level > 0)
                            yield return (node, symbol);
                    }
                }
            }
        }
    }
}
