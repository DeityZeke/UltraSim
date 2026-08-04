using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UltraSim.ECS.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class EcsSchemaGenerator : IIncrementalGenerator
{
    private const string SchemaAttributeName = "UltraSim.ECS.EcsSchemaAttribute";
    private const string ComponentAttributeName = "UltraSim.ECS.EcsComponentAttribute";
    private const string ComponentInterfaceName = "UltraSim.ECS.IComponent<TSelf>";
    private const int MaximumComponentCount = ushort.MaxValue;

    private static readonly DiagnosticDescriptor InvalidSchemaDeclaration = new(
        "ECSGEN001",
        "Invalid ECS schema declaration",
        "Schema '{0}' must be a top-level, non-generic static partial class",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidComponent = new(
        "ECSGEN002",
        "Invalid ECS component",
        "Component '{0}' {1}",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateCanonicalKey = new(
        "ECSGEN003",
        "Duplicate canonical component key",
        "Components '{0}' and '{1}' both use canonical key '{2}'",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicatePersistenceId = new(
        "ECSGEN004",
        "Duplicate component persistence ID",
        "Components '{0}' and '{1}' both use persistence ID {2}",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidComponentIdentity = new(
        "ECSGEN005",
        "Invalid component identity",
        "Component '{0}' has {1}",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FieldBearingTag = new(
        "ECSGEN006",
        "Tag component contains data",
        "Tag component '{0}' contains instance fields; tags must have no data fields",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ComponentLimitExceeded = new(
        "ECSGEN007",
        "Schema component limit exceeded",
        "Schema '{0}' declares {1} components; the maximum is {2} because ComponentId 0xFFFF is reserved",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateComponentType = new(
        "ECSGEN008",
        "Duplicate component type",
        "Schema '{0}' includes component type '{1}' more than once",
        "UltraSim.ECS.Schema",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<SchemaRequest> schemas = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SchemaAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => CreateRequest(attributeContext));

        context.RegisterSourceOutput(schemas, static (sourceContext, request) =>
            Generate(sourceContext, request));
    }

    private static SchemaRequest CreateRequest(GeneratorAttributeSyntaxContext context)
    {
        var schemaType = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        var componentTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();

        if (attribute.ConstructorArguments.Length == 1)
        {
            TypedConstant argument = attribute.ConstructorArguments[0];
            foreach (TypedConstant item in argument.Values)
            {
                if (item.Value is ITypeSymbol type)
                    componentTypes.Add(type);
            }
        }

        Location location = context.TargetNode.GetLocation();
        return new SchemaRequest(schemaType, componentTypes.ToImmutable(), location);
    }

    private static void Generate(SourceProductionContext context, SchemaRequest request)
    {
        if (!IsValidSchemaDeclaration(request.SchemaType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidSchemaDeclaration,
                request.Location,
                request.SchemaType.ToDisplayString()));
            return;
        }

        if (request.ComponentTypes.Length > MaximumComponentCount)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ComponentLimitExceeded,
                request.Location,
                request.SchemaType.ToDisplayString(),
                request.ComponentTypes.Length,
                MaximumComponentCount));
            return;
        }

        var components = new List<ComponentModel>(request.ComponentTypes.Length);
        var seenTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        bool hasErrors = false;

        foreach (ITypeSymbol type in request.ComponentTypes)
        {
            if (!seenTypes.Add(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateComponentType,
                    request.Location,
                    request.SchemaType.ToDisplayString(),
                    type.ToDisplayString()));
                hasErrors = true;
                continue;
            }

            if (type is not INamedTypeSymbol namedType)
            {
                ReportInvalidComponent(context, request.Location, type, "must be a named struct type");
                hasErrors = true;
                continue;
            }

            ComponentModel? component = CreateComponentModel(context, request.Location, namedType);
            if (component is null)
            {
                hasErrors = true;
                continue;
            }

            components.Add(component);
        }

        foreach (IGrouping<string, ComponentModel> duplicate in components
                     .GroupBy(static component => component.CanonicalKey, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            ComponentModel[] pair = duplicate.Take(2).ToArray();
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicateCanonicalKey,
                request.Location,
                pair[0].DisplayName,
                pair[1].DisplayName,
                duplicate.Key));
            hasErrors = true;
        }

        foreach (IGrouping<uint, ComponentModel> duplicate in components
                     .Where(static component => component.PersistenceId != 0)
                     .GroupBy(static component => component.PersistenceId)
                     .Where(static group => group.Count() > 1))
        {
            ComponentModel[] pair = duplicate.Take(2).ToArray();
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicatePersistenceId,
                request.Location,
                pair[0].DisplayName,
                pair[1].DisplayName,
                duplicate.Key));
            hasErrors = true;
        }

        if (hasErrors)
            return;

        components.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.CanonicalKey, right.CanonicalKey));

        SchemaFingerprint fingerprint = ComputeSchemaFingerprint(components);
        string source = Render(request.SchemaType, components, fingerprint);
        string schemaDisplayName = request.SchemaType.ToDisplayString();
        string hintName = SanitizeHintName(schemaDisplayName) + "_" +
            ComputeStableHash(schemaDisplayName).ToString("x16", CultureInfo.InvariantCulture) +
            ".EcsSchema.g.cs";
        context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
    }

    private static ComponentModel? CreateComponentModel(
        SourceProductionContext context,
        Location location,
        INamedTypeSymbol type)
    {
        string displayName = type.ToDisplayString();
        if (type.TypeKind != TypeKind.Struct || !type.IsUnmanagedType || type.IsRefLikeType)
        {
            ReportInvalidComponent(context, location, type, "must be a non-ref unmanaged struct");
            return null;
        }

        bool hasSelfInterface = type.AllInterfaces.Any(interfaceType =>
            interfaceType.OriginalDefinition.ToDisplayString() == ComponentInterfaceName &&
            interfaceType.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], type));
        if (!hasSelfInterface)
        {
            ReportInvalidComponent(
                context,
                location,
                type,
                "must implement IComponent<TSelf> with itself as TSelf");
            return null;
        }

        AttributeData? attribute = type.GetAttributes().FirstOrDefault(static candidate =>
            candidate.AttributeClass?.ToDisplayString() == ComponentAttributeName);
        if (attribute is null || attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string canonicalKey)
        {
            ReportInvalidComponent(context, location, type, "must declare [EcsComponent] with a canonical key");
            return null;
        }

        ushort version = 1;
        bool isTag = false;
        bool isEnableable = false;
        uint persistenceId = 0;
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Version" when argument.Value.Value is ushort value:
                    version = value;
                    break;
                case "IsTag" when argument.Value.Value is bool value:
                    isTag = value;
                    break;
                case "IsEnableable" when argument.Value.Value is bool value:
                    isEnableable = value;
                    break;
                case "PersistenceId" when argument.Value.Value is uint value:
                    persistenceId = value;
                    break;
            }
        }

        if (!IsCanonicalKey(canonicalKey))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidComponentIdentity,
                location,
                displayName,
                $"invalid canonical key '{canonicalKey}'"));
            return null;
        }

        if (version == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidComponentIdentity,
                location,
                displayName,
                "reserved version zero"));
            return null;
        }

        ImmutableArray<IFieldSymbol> fields = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static field => !field.IsStatic)
            .ToImmutableArray();
        if (isTag && fields.Length != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(FieldBearingTag, location, displayName));
            return null;
        }

        byte flags = 0;
        if (isTag)
            flags |= 1;
        if (isEnableable)
            flags |= 2;
        if (persistenceId != 0)
            flags |= 4;

        string fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string fieldSignature = BuildFieldSignature(fields);
        return new ComponentModel(
            displayName,
            fullyQualifiedName,
            canonicalKey,
            version,
            flags,
            persistenceId,
            ComputeStableHash(canonicalKey),
            fieldSignature);
    }

    private static void ReportInvalidComponent(
        SourceProductionContext context,
        Location location,
        ITypeSymbol type,
        string reason) => context.ReportDiagnostic(Diagnostic.Create(
            InvalidComponent,
            location,
            type.ToDisplayString(),
            reason));

    private static bool IsValidSchemaDeclaration(INamedTypeSymbol schemaType)
    {
        if (schemaType.TypeKind != TypeKind.Class ||
            !schemaType.IsStatic ||
            schemaType.Arity != 0 ||
            schemaType.ContainingType is not null)
        {
            return false;
        }

        return schemaType.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    private static bool IsCanonicalKey(string value)
    {
        if (string.IsNullOrEmpty(value) || !IsLowerAlphaNumeric(value[0]))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if (!IsLowerAlphaNumeric(character) &&
                character != '.' &&
                character != '_' &&
                character != '/' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerAlphaNumeric(char value) =>
        (value >= 'a' && value <= 'z') || (value >= '0' && value <= '9');

    private static string BuildFieldSignature(ImmutableArray<IFieldSymbol> fields)
    {
        var builder = new StringBuilder();
        foreach (IFieldSymbol field in fields)
        {
            string name = field.AssociatedSymbol is IPropertySymbol property
                ? "P:" + property.Name
                : "F:" + field.Name;
            builder.Append(name)
                .Append(':')
                .Append(field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(';');
        }

        return builder.ToString();
    }

    private static ulong ComputeStableHash(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        foreach (char character in value)
            hash = (hash ^ character) * prime;
        return hash;
    }

    private static SchemaFingerprint ComputeSchemaFingerprint(IReadOnlyList<ComponentModel> components)
    {
        var descriptor = new StringBuilder();
        foreach (ComponentModel component in components)
        {
            descriptor
                .Append(component.CanonicalKey).Append('|')
                .Append(component.Version.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.Flags.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.PersistenceId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(component.FullyQualifiedName).Append('|')
                .Append(component.FieldSignature).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(descriptor.ToString());
        byte[] hash;
        using (SHA256 sha256 = SHA256.Create())
            hash = sha256.ComputeHash(bytes);

        ulong low = ReadUInt64LittleEndian(hash, 0);
        ulong high = ReadUInt64LittleEndian(hash, 8);
        if ((low | high) == 0)
            high = 1;
        return new SchemaFingerprint(low, high);
    }

    private static ulong ReadUInt64LittleEndian(byte[] bytes, int offset)
    {
        ulong value = 0;
        for (int index = 0; index < sizeof(ulong); index++)
            value |= (ulong)bytes[offset + index] << (index * 8);
        return value;
    }

    private static string Render(
        INamedTypeSymbol schemaType,
        IReadOnlyList<ComponentModel> components,
        SchemaFingerprint fingerprint)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />")
            .AppendLine("#nullable enable");

        if (!schemaType.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ")
                .Append(schemaType.ContainingNamespace.ToDisplayString())
                .AppendLine(";")
                .AppendLine();
        }

        string accessibility = schemaType.DeclaredAccessibility == Accessibility.Public
            ? "public"
            : "internal";
        source.Append(accessibility)
            .Append(" static partial class ")
            .Append(EscapeIdentifier(schemaType.Name))
            .AppendLine()
            .AppendLine("{")
            .AppendLine("    private static readonly global::UltraSim.ECS.ComponentRegistration[] s_generatedComponentRegistrations =")
            .AppendLine("    new global::UltraSim.ECS.ComponentRegistration[]")
            .AppendLine("    {");

        for (int index = 0; index < components.Count; index++)
        {
            ComponentModel component = components[index];
            source.Append("        global::UltraSim.ECS.ComponentRegistration.Create<")
                .Append(component.FullyQualifiedName)
                .AppendLine(">(")
                .Append("            new global::UltraSim.ECS.ComponentId(")
                .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("),")
                .Append("            \"").Append(EscapeString(component.CanonicalKey)).AppendLine("\",")
                .Append("            0x").Append(component.StableKeyHash.ToString("x16", CultureInfo.InvariantCulture)).AppendLine("UL,")
                .Append("            ").Append(component.Version.ToString(CultureInfo.InvariantCulture)).AppendLine(",")
                .Append("            (global::UltraSim.ECS.ComponentFlags)")
                .Append(component.Flags.ToString(CultureInfo.InvariantCulture)).AppendLine(",")
                .Append("            ").Append(component.PersistenceId.ToString(CultureInfo.InvariantCulture)).AppendLine("U),");
        }

        source.AppendLine("    };")
            .AppendLine()
            .Append("    public static global::UltraSim.ECS.SchemaId GeneratedSchemaId => new(0x")
            .Append(fingerprint.Low.ToString("x16", CultureInfo.InvariantCulture))
            .Append("UL, 0x")
            .Append(fingerprint.High.ToString("x16", CultureInfo.InvariantCulture))
            .AppendLine("UL);")
            .AppendLine()
            .AppendLine("    public static global::System.ReadOnlySpan<global::UltraSim.ECS.ComponentRegistration> GeneratedComponentRegistrations =>")
            .AppendLine("        s_generatedComponentRegistrations;")
            .AppendLine()
            .AppendLine("    public static global::UltraSim.ECS.ComponentSchema CreateSchema() =>")
            .AppendLine("        global::UltraSim.ECS.ComponentSchema.Create(GeneratedSchemaId, s_generatedComponentRegistrations);")
            .AppendLine()
            .AppendLine("    public static global::UltraSim.ECS.ComponentId GetComponentId<T>()")
            .AppendLine("        where T : unmanaged, global::UltraSim.ECS.IComponent<T>")
            .AppendLine("    {");

        for (int index = 0; index < components.Count; index++)
        {
            source.Append("        if (typeof(T) == typeof(")
                .Append(components[index].FullyQualifiedName)
                .AppendLine("))")
                .Append("            return new global::UltraSim.ECS.ComponentId(")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine(");");
        }

        source.AppendLine()
            .AppendLine("        return global::UltraSim.ECS.ComponentId.Invalid;")
            .AppendLine("    }")
            .AppendLine("}");
        return source.ToString();
    }

    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "@" + value : value;

    private static string EscapeString(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    private static string SanitizeHintName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private sealed class SchemaRequest
    {
        internal SchemaRequest(
            INamedTypeSymbol schemaType,
            ImmutableArray<ITypeSymbol> componentTypes,
            Location location)
        {
            SchemaType = schemaType;
            ComponentTypes = componentTypes;
            Location = location;
        }

        internal INamedTypeSymbol SchemaType { get; }

        internal ImmutableArray<ITypeSymbol> ComponentTypes { get; }

        internal Location Location { get; }
    }

    private sealed class ComponentModel
    {
        internal ComponentModel(
            string displayName,
            string fullyQualifiedName,
            string canonicalKey,
            ushort version,
            byte flags,
            uint persistenceId,
            ulong stableKeyHash,
            string fieldSignature)
        {
            DisplayName = displayName;
            FullyQualifiedName = fullyQualifiedName;
            CanonicalKey = canonicalKey;
            Version = version;
            Flags = flags;
            PersistenceId = persistenceId;
            StableKeyHash = stableKeyHash;
            FieldSignature = fieldSignature;
        }

        internal string DisplayName { get; }
        internal string FullyQualifiedName { get; }
        internal string CanonicalKey { get; }
        internal ushort Version { get; }
        internal byte Flags { get; }
        internal uint PersistenceId { get; }
        internal ulong StableKeyHash { get; }
        internal string FieldSignature { get; }
    }

    private readonly struct SchemaFingerprint
    {
        internal SchemaFingerprint(ulong low, ulong high)
        {
            Low = low;
            High = high;
        }

        internal ulong Low { get; }
        internal ulong High { get; }
    }
}
