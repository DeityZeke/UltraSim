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
    private const string EntityTypeName = "UltraSim.ECS.Entity";
    private const string PersistentKeyTypeName = "UltraSim.ECS.PersistentEntityKey";
    private const string EntityReferenceAttributeName = "UltraSim.ECS.PersistenceEntityReferenceAttribute";
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

    private static readonly DiagnosticDescriptor CustomPersistenceCodecRequired = new(
        "ECSGEN009",
        "Persistent component requires a custom codec",
        "Component '{0}' uses unsupported or inaccessible persistent member '{1}'; register a custom codec",
        "UltraSim.ECS.Persistence",
        DiagnosticSeverity.Warning,
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
        byte persistenceMode = 0;
        bool persistEnableState = false;
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
                case "PersistenceMode" when argument.Value.Value is byte value:
                    persistenceMode = value;
                    break;
                case "PersistenceMode" when argument.Value.Value is int integerValue:
                    persistenceMode = checked((byte)integerValue);
                    break;
                case "PersistEnableState" when argument.Value.Value is bool value:
                    persistEnableState = value;
                    break;
            }
        }

        if (persistenceMode == 0)
            persistenceMode = persistenceId == 0 ? (byte)1 : isTag ? (byte)3 : (byte)2;
        if (persistenceMode is < 1 or > 4 ||
            (persistenceMode == 1 && persistenceId != 0) ||
            (persistenceMode != 1 && persistenceId == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidComponentIdentity,
                location,
                displayName,
                "an invalid persistence ID/mode combination"));
            return null;
        }
        if (persistEnableState && !isEnableable)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidComponentIdentity,
                location,
                displayName,
                "PersistEnableState set on a non-enableable component"));
            return null;
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
        if (persistenceMode == 2)
            flags |= 4;
        if (persistEnableState)
            flags |= 8;

        string fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string fieldSignature = BuildFieldSignature(fields);
        ImmutableArray<PersistenceMemberModel> persistenceMembers = ImmutableArray<PersistenceMemberModel>.Empty;
        bool codecSupported = true;
        if (persistenceMode == 2 && !isTag)
        {
            var memberBuilder = ImmutableArray.CreateBuilder<PersistenceMemberModel>();
            foreach (ISymbol member in type.GetMembers())
            {
                ITypeSymbol? memberType = null;
                string? memberName = null;
                bool entityReference = false;
                bool inaccessible = false;
                if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared)
                {
                    memberType = field.Type;
                    memberName = field.Name;
                    entityReference = HasAttribute(field, EntityReferenceAttributeName);
                    inaccessible = !IsPersistenceMemberAccessible(field.DeclaredAccessibility) || field.IsReadOnly;
                }
                else if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer)
                {
                    memberType = property.Type;
                    memberName = property.Name;
                    entityReference = HasAttribute(property, EntityReferenceAttributeName);
                    inaccessible = property.GetMethod is null || property.SetMethod is null ||
                        !IsPersistenceMemberAccessible(property.GetMethod.DeclaredAccessibility) ||
                        !IsPersistenceMemberAccessible(property.SetMethod.DeclaredAccessibility);
                }
                if (memberType is null || memberName is null)
                    continue;
                if (inaccessible)
                {
                    codecSupported = false;
                    context.ReportDiagnostic(Diagnostic.Create(CustomPersistenceCodecRequired,
                        member.Locations.FirstOrDefault() ?? location, displayName, memberName));
                    continue;
                }
                if (!TryCreatePersistenceMember(memberName, memberType, entityReference, out PersistenceMemberModel model))
                {
                    codecSupported = false;
                    context.ReportDiagnostic(Diagnostic.Create(CustomPersistenceCodecRequired, location, displayName, memberName));
                    continue;
                }
                memberBuilder.Add(model);
            }
            persistenceMembers = memberBuilder.OrderBy(static member => member.Name, StringComparer.Ordinal).ToImmutableArray();
        }
        int encodedSize = persistenceMembers.Sum(static member => member.EncodedSize);
        SchemaFingerprint persistenceFingerprint = ComputePersistenceFingerprint(
            canonicalKey, version, persistenceMode, persistenceMembers);
        return new ComponentModel(
            displayName,
            fullyQualifiedName,
            canonicalKey,
            version,
            flags,
            persistenceId,
            persistenceMode,
            ComputeStableHash(canonicalKey),
            fieldSignature,
            persistenceMembers,
            codecSupported,
            encodedSize,
            persistenceFingerprint);
    }

    private static bool HasAttribute(ISymbol symbol, string name) => symbol.GetAttributes().Any(
        attribute => attribute.AttributeClass?.ToDisplayString() == name);

    private static bool IsPersistenceMemberAccessible(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool TryCreatePersistenceMember(
        string name,
        ITypeSymbol type,
        bool entityReference,
        out PersistenceMemberModel model)
    {
        ITypeSymbol serializedType = type;
        string typeName = type.ToDisplayString();
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType && enumType.EnumUnderlyingType is not null)
            serializedType = enumType.EnumUnderlyingType;
        string special = serializedType.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "int16",
            SpecialType.System_UInt16 => "uint16",
            SpecialType.System_Int32 => "int32",
            SpecialType.System_UInt32 => "uint32",
            SpecialType.System_Int64 => "int64",
            SpecialType.System_UInt64 => "uint64",
            SpecialType.System_Single => "single",
            SpecialType.System_Double => "double",
            _ => string.Empty,
        };
        int size = special switch
        {
            "bool" or "byte" or "sbyte" => 1,
            "int16" or "uint16" => 2,
            "int32" or "uint32" or "single" => 4,
            "int64" or "uint64" or "double" => 8,
            _ => 0,
        };
        if (typeName == EntityTypeName)
        {
            if (!entityReference) { model = default!; return false; }
            model = new PersistenceMemberModel(name, "entity", type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), 4);
            return true;
        }
        if (typeName == PersistentKeyTypeName)
        {
            model = new PersistenceMemberModel(name, "persistent-key", type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), 16);
            return true;
        }
        if (size == 0 || (entityReference && typeName != EntityTypeName)) { model = default!; return false; }
        model = new PersistenceMemberModel(name, special, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), size);
        return true;
    }

    private static SchemaFingerprint ComputePersistenceFingerprint(
        string canonicalKey,
        ushort version,
        byte mode,
        ImmutableArray<PersistenceMemberModel> members)
    {
        var descriptor = new StringBuilder(canonicalKey).Append('|').Append(version).Append('|').Append(mode);
        foreach (PersistenceMemberModel member in members)
            descriptor.Append('|').Append(member.Name).Append(':').Append(member.Kind).Append(':').Append(member.TypeName);
        byte[] bytes = Encoding.UTF8.GetBytes(descriptor.ToString());
        byte[] hash;
        using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
        ulong low = ReadUInt64LittleEndian(hash, 0), high = ReadUInt64LittleEndian(hash, 8);
        if ((low | high) == 0) high = 1;
        return new SchemaFingerprint(low, high);
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
                .Append(component.PersistenceMode.ToString(CultureInfo.InvariantCulture)).Append('|')
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
                .Append("            ").Append(component.PersistenceId.ToString(CultureInfo.InvariantCulture)).AppendLine("U,")
                .Append("            (global::UltraSim.ECS.ComponentPersistenceMode)")
                .Append(component.PersistenceMode.ToString(CultureInfo.InvariantCulture)).AppendLine("),");
        }

        source.AppendLine("    };")
            .AppendLine();

        for (int index = 0; index < components.Count; index++)
        {
            ComponentModel component = components[index];
            if (component.PersistenceMode == 2 && component.CodecSupported)
                RenderPersistenceCodec(source, component, index);
        }

        source
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
            .AppendLine("    public static global::UltraSim.ECS.PersistenceCatalog CreatePersistenceCatalog(")
            .AppendLine("        global::System.Action<global::UltraSim.ECS.PersistenceCatalogBuilder>? configure = null)")
            .AppendLine("    {")
            .AppendLine("        var builder = new global::UltraSim.ECS.PersistenceCatalogBuilder(CreateSchema());");

        for (int index = 0; index < components.Count; index++)
        {
            ComponentModel component = components[index];
            if (component.PersistenceMode != 2 || !component.CodecSupported)
                continue;
            source.Append("        builder.Register<").Append(component.FullyQualifiedName).Append(">(")
                .Append("new global::UltraSim.ECS.PersistenceSchemaFingerprint(0x")
                .Append(component.PersistenceFingerprint.Low.ToString("x16", CultureInfo.InvariantCulture))
                .Append("UL, 0x")
                .Append(component.PersistenceFingerprint.High.ToString("x16", CultureInfo.InvariantCulture))
                .Append("UL), new PersistenceCodec_").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("());");
        }

        source.AppendLine("        configure?.Invoke(builder);")
            .AppendLine("        return builder.Build();")
            .AppendLine("    }")
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

    private static void RenderPersistenceCodec(StringBuilder source, ComponentModel component, int index)
    {
        source.Append("    private sealed class PersistenceCodec_").Append(index)
            .Append(" : global::UltraSim.ECS.IPersistenceComponentCodec<")
            .Append(component.FullyQualifiedName).Append(">, global::UltraSim.ECS.IPersistenceBatchComponentCodec<")
            .Append(component.FullyQualifiedName).AppendLine(">")
            .AppendLine("    {")
            .Append("        public int EncodedSize => ").Append(component.EncodedSize).AppendLine(";")
            .Append("        public void Encode(in ").Append(component.FullyQualifiedName)
            .AppendLine(" value, global::System.Span<byte> destination, global::UltraSim.ECS.PersistenceWriteContext context)")
            .AppendLine("        {")
            .Append("            if (destination.Length != ").Append(component.EncodedSize)
            .AppendLine(") throw new global::System.ArgumentException(\"Persistence destination size mismatch.\", nameof(destination));");
        int offset = 0;
        foreach (PersistenceMemberModel member in component.PersistenceMembers)
        {
            RenderEncodeMember(source, member, offset);
            offset += member.EncodedSize;
        }
        source.AppendLine("        }")
            .Append("        public void Decode(global::System.ReadOnlySpan<byte> source, ref ")
            .Append(component.FullyQualifiedName)
            .AppendLine(" value, global::UltraSim.ECS.PersistenceReadContext context)")
            .AppendLine("        {")
            .Append("            if (source.Length != ").Append(component.EncodedSize)
            .AppendLine(") throw new global::System.ArgumentException(\"Persistence source size mismatch.\", nameof(source));");
        offset = 0;
        foreach (PersistenceMemberModel member in component.PersistenceMembers)
        {
            RenderDecodeMember(source, member, offset);
            offset += member.EncodedSize;
        }
        source.AppendLine("        }")
            .Append("        public void EncodeBatch(global::System.ReadOnlySpan<").Append(component.FullyQualifiedName)
            .AppendLine("> values, global::System.Span<byte> destination, global::UltraSim.ECS.PersistenceWriteContext context)")
            .AppendLine("        {")
            .AppendLine("            if (destination.Length != checked(values.Length * EncodedSize)) throw new global::System.ArgumentException(\"Persistence batch size mismatch.\");")
            .AppendLine("            for (int index = 0; index < values.Length; index++) Encode(values[index], destination.Slice(index * EncodedSize, EncodedSize), context);")
            .AppendLine("        }")
            .Append("        public void DecodeBatch(global::System.ReadOnlySpan<byte> source, global::System.Span<")
            .Append(component.FullyQualifiedName).AppendLine("> values, global::UltraSim.ECS.PersistenceReadContext context)")
            .AppendLine("        {")
            .AppendLine("            if (source.Length != checked(values.Length * EncodedSize)) throw new global::System.ArgumentException(\"Persistence batch size mismatch.\");")
            .AppendLine("            for (int index = 0; index < values.Length; index++) Decode(source.Slice(index * EncodedSize, EncodedSize), ref values[index], context);")
            .AppendLine("        }")
            .AppendLine("    }")
            .AppendLine();
    }

    private static void RenderEncodeMember(StringBuilder source, PersistenceMemberModel member, int offset)
    {
        string value = "value." + EscapeIdentifier(member.Name);
        string slice = $"destination.Slice({offset}, {member.EncodedSize})";
        switch (member.Kind)
        {
            case "bool": source.Append("            destination[").Append(offset).Append("] = ").Append(value).AppendLine(" ? (byte)1 : (byte)0;"); break;
            case "byte": source.Append("            destination[").Append(offset).Append("] = (byte)").Append(value).AppendLine(";"); break;
            case "sbyte": source.Append("            destination[").Append(offset).Append("] = unchecked((byte)").Append(value).AppendLine(");"); break;
            case "int16": RenderBinaryWrite(source, "WriteInt16LittleEndian", slice, "(short)" + value); break;
            case "uint16": RenderBinaryWrite(source, "WriteUInt16LittleEndian", slice, "(ushort)" + value); break;
            case "int32": RenderBinaryWrite(source, "WriteInt32LittleEndian", slice, "(int)" + value); break;
            case "uint32": RenderBinaryWrite(source, "WriteUInt32LittleEndian", slice, "(uint)" + value); break;
            case "int64": RenderBinaryWrite(source, "WriteInt64LittleEndian", slice, "(long)" + value); break;
            case "uint64": RenderBinaryWrite(source, "WriteUInt64LittleEndian", slice, "(ulong)" + value); break;
            case "single": RenderBinaryWrite(source, "WriteInt32LittleEndian", slice, "global::System.BitConverter.SingleToInt32Bits(" + value + ")"); break;
            case "double": RenderBinaryWrite(source, "WriteInt64LittleEndian", slice, "global::System.BitConverter.DoubleToInt64Bits(" + value + ")"); break;
            case "entity": RenderBinaryWrite(source, "WriteUInt32LittleEndian", slice, "context.EncodeEntityReference(" + value + ").Value"); break;
            case "persistent-key":
                RenderBinaryWrite(source, "WriteUInt64LittleEndian", $"destination.Slice({offset}, 8)", value + ".World.Value");
                RenderBinaryWrite(source, "WriteUInt64LittleEndian", $"destination.Slice({offset + 8}, 8)", value + ".Entity.Value");
                break;
        }
    }

    private static void RenderBinaryWrite(StringBuilder source, string method, string destination, string value) =>
        source.Append("            global::System.Buffers.Binary.BinaryPrimitives.").Append(method)
            .Append('(').Append(destination).Append(", ").Append(value).AppendLine(");");

    private static void RenderDecodeMember(StringBuilder source, PersistenceMemberModel member, int offset)
    {
        string target = "value." + EscapeIdentifier(member.Name);
        string slice = $"source.Slice({offset}, {member.EncodedSize})";
        string read = member.Kind switch
        {
            "bool" => $"source[{offset}] switch {{ 0 => false, 1 => true, _ => throw new global::UltraSim.ECS.PersistenceValidationException(\"Boolean persistence payload is not canonical.\") }}",
            "byte" => $"source[{offset}]",
            "sbyte" => $"unchecked((sbyte)source[{offset}])",
            "int16" => "global::System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(" + slice + ")",
            "uint16" => "global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(" + slice + ")",
            "int32" => "global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(" + slice + ")",
            "uint32" => "global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(" + slice + ")",
            "int64" => "global::System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(" + slice + ")",
            "uint64" => "global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(" + slice + ")",
            "single" => "global::System.BitConverter.Int32BitsToSingle(global::System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(" + slice + "))",
            "double" => "global::System.BitConverter.Int64BitsToDouble(global::System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(" + slice + "))",
            "entity" => "context.DecodeEntityReference(new global::UltraSim.ECS.SaveEntityOrdinal(global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(" + slice + ")))",
            _ => string.Empty,
        };
        if (member.Kind == "persistent-key")
        {
            source.Append("            ulong world_").Append(offset).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(")
                .Append(offset).AppendLine(", 8));")
                .Append("            ulong entity_").Append(offset).Append(" = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(")
                .Append(offset + 8).AppendLine(", 8));")
                .Append("            ").Append(target).Append(" = (world_").Append(offset).Append(" | entity_").Append(offset)
                .Append(") == 0 ? default : new global::UltraSim.ECS.PersistentEntityKey(new global::UltraSim.ECS.PersistentWorldId(world_")
                .Append(offset).Append("), new global::UltraSim.ECS.PersistentEntityId(entity_").Append(offset).AppendLine("));");
            return;
        }
        source.Append("            ").Append(target).Append(" = (").Append(member.TypeName).Append(")(").Append(read).AppendLine(");");
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
            byte persistenceMode,
            ulong stableKeyHash,
            string fieldSignature,
            ImmutableArray<PersistenceMemberModel> persistenceMembers,
            bool codecSupported,
            int encodedSize,
            SchemaFingerprint persistenceFingerprint)
        {
            DisplayName = displayName;
            FullyQualifiedName = fullyQualifiedName;
            CanonicalKey = canonicalKey;
            Version = version;
            Flags = flags;
            PersistenceId = persistenceId;
            PersistenceMode = persistenceMode;
            StableKeyHash = stableKeyHash;
            FieldSignature = fieldSignature;
            PersistenceMembers = persistenceMembers;
            CodecSupported = codecSupported;
            EncodedSize = encodedSize;
            PersistenceFingerprint = persistenceFingerprint;
        }

        internal string DisplayName { get; }
        internal string FullyQualifiedName { get; }
        internal string CanonicalKey { get; }
        internal ushort Version { get; }
        internal byte Flags { get; }
        internal uint PersistenceId { get; }
        internal byte PersistenceMode { get; }
        internal ulong StableKeyHash { get; }
        internal string FieldSignature { get; }
        internal ImmutableArray<PersistenceMemberModel> PersistenceMembers { get; }
        internal bool CodecSupported { get; }
        internal int EncodedSize { get; }
        internal SchemaFingerprint PersistenceFingerprint { get; }
    }

    private sealed class PersistenceMemberModel
    {
        internal PersistenceMemberModel(string name, string kind, string typeName, int encodedSize)
        {
            Name = name;
            Kind = kind;
            TypeName = typeName;
            EncodedSize = encodedSize;
        }
        internal string Name { get; }
        internal string Kind { get; }
        internal string TypeName { get; }
        internal int EncodedSize { get; }
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
