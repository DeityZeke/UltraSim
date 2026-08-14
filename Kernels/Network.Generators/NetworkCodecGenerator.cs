using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UltraSim.Network.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class NetworkCodecGenerator : IIncrementalGenerator
{
    private const string ProtocolAttribute = "UltraSim.Network.NetworkProtocolAttribute";
    private const string MessageAttribute = "UltraSim.Network.NetworkMessageAttribute";

    private static readonly DiagnosticDescriptor InvalidDeclaration = new(
        "NETGEN001", "Invalid network protocol declaration", "{0}", "UltraSim.Network.Codec",
        DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor UnsupportedField = new(
        "NETGEN002", "Unsupported network message field", "Message '{0}' field '{1}' {2}",
        "UltraSim.Network.Codec", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateIdentity = new(
        "NETGEN003", "Duplicate network message identity", "Messages '{0}' and '{1}' share {2}",
        "UltraSim.Network.Codec", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, static (production, compilation) =>
            Execute(compilation, production));
    }

    private static void Execute(Compilation compilation, SourceProductionContext context)
    {
        var protocols = new List<(INamedTypeSymbol Symbol, ushort Version)>();
        var messages = new List<Message>();
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol) continue;
                AttributeData? protocol = FindAttribute(symbol, ProtocolAttribute);
                if (protocol is not null)
                {
                    ushort version = Convert.ToUInt16(protocol.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                    protocols.Add((symbol, version));
                }
                AttributeData? message = FindAttribute(symbol, MessageAttribute);
                if (message is not null && TryBuildMessage(symbol, message, context, out Message built))
                    messages.Add(built);
            }
        }

        if (protocols.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, Location.None,
                $"Exactly one top-level static partial protocol class is required; found {protocols.Count}."));
            return;
        }
        INamedTypeSymbol protocolSymbol = protocols[0].Symbol;
        if (!protocolSymbol.IsStatic || protocolSymbol.ContainingType is not null || !IsPartial(protocolSymbol) ||
            protocolSymbol.DeclaredAccessibility != Accessibility.Public || protocolSymbol.Arity != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, protocolSymbol.Locations.FirstOrDefault(),
                $"Protocol '{protocolSymbol.Name}' must be a public, non-generic, top-level static partial class."));
            return;
        }

        messages.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        for (int index = 0; index < messages.Count; index++)
        {
            for (int previous = 0; previous < index; previous++)
            {
                if (messages[previous].Id == messages[index].Id)
                    ReportDuplicate(context, messages[previous], messages[index], $"message ID {messages[index].Id}");
                if (string.Equals(messages[previous].Key, messages[index].Key, StringComparison.Ordinal))
                    ReportDuplicate(context, messages[previous], messages[index], $"canonical key '{messages[index].Key}'");
            }
        }
        if (messages.Count == 0) return;

        foreach (Message message in messages)
            context.AddSource($"{HintStem(message.Symbol)}.NetworkCodec.g.cs",
                SourceText.From(EmitCodec(message), Encoding.UTF8));
        context.AddSource($"{HintStem(protocolSymbol)}.NetworkManifest.g.cs",
            SourceText.From(EmitManifest(protocolSymbol, protocols[0].Version, messages), Encoding.UTF8));
    }

    private static bool TryBuildMessage(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        SourceProductionContext context,
        out Message message)
    {
        message = default!;
        if (symbol.TypeKind != TypeKind.Struct || symbol.ContainingType is not null || !IsPartial(symbol) ||
            symbol.DeclaringSyntaxReferences.Length != 1 ||
            symbol.DeclaredAccessibility != Accessibility.Public || symbol.Arity != 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, symbol.Locations.FirstOrDefault(),
                $"Network message '{symbol.Name}' must be one public, non-generic, top-level partial struct declaration."));
            return false;
        }
        ushort id = Convert.ToUInt16(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
        string key = (string?)attribute.ConstructorArguments[1].Value ?? string.Empty;
        ushort version = Convert.ToUInt16(attribute.ConstructorArguments[2].Value, CultureInfo.InvariantCulture);
        byte lane = Convert.ToByte(attribute.ConstructorArguments[3].Value, CultureInfo.InvariantCulture);
        var fields = new List<Field>();
        foreach (IFieldSymbol field in symbol.GetMembers().OfType<IFieldSymbol>()
                     .Where(static field => !field.IsStatic && !field.IsImplicitlyDeclared)
                     .OrderBy(static field => field.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
        {
            if (field.DeclaredAccessibility != Accessibility.Public || field.IsReadOnly ||
                !TryDescribe(field.Type, out string writer, out string reader, out int bytes, out string? conversion,
                    out string? validation))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedField, field.Locations.FirstOrDefault(),
                    symbol.Name, field.Name,
                    "must be a mutable public fixed-width primitive or enum with a declared value set"));
                return false;
            }
            fields.Add(new Field(field.Name, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                writer, reader, bytes, conversion, validation));
        }
        if (fields.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, symbol.Locations.FirstOrDefault(),
                $"Network message '{symbol.Name}' must contain at least one encoded field."));
            return false;
        }
        message = new Message(symbol, id, key, version, lane, fields);
        return true;
    }

    private static bool TryDescribe(
        ITypeSymbol type,
        out string writer,
        out string reader,
        out int bytes,
        out string? conversion,
        out string? validation)
    {
        conversion = null;
        validation = null;
        ITypeSymbol encoded = type;
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumeration)
        {
            encoded = enumeration.EnumUnderlyingType!;
            conversion = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool signed = encoded.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16 or
                SpecialType.System_Int32 or SpecialType.System_Int64;
            ulong[] numericValues = enumeration.GetMembers().OfType<IFieldSymbol>()
                .Where(static field => field.HasConstantValue)
                .Select(field => signed
                    ? unchecked((ulong)Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture))
                    : Convert.ToUInt64(field.ConstantValue, CultureInfo.InvariantCulture))
                .Distinct().ToArray();
            if (numericValues.Length == 0) { writer = reader = string.Empty; bytes = 0; return false; }
            bool flags = enumeration.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");
            int encodedBits = encoded.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte ? 8 :
                encoded.SpecialType is SpecialType.System_Int16 or SpecialType.System_UInt16 ? 16 :
                encoded.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32 ? 32 : 64;
            ulong encodedMask = encodedBits == 64 ? ulong.MaxValue : (1UL << encodedBits) - 1;
            for (int index = 0; index < numericValues.Length; index++) numericValues[index] &= encodedMask;
            if (flags)
            {
                ulong mask = 0;
                foreach (ulong numericValue in numericValues) mask |= numericValue;
                validation = $"((raw & {encodedMask.ToString(CultureInfo.InvariantCulture)}UL) & ~{mask.ToString(CultureInfo.InvariantCulture)}UL) != 0";
            }
            else
            {
                validation = string.Join(" && ", numericValues.Select(value =>
                    $"(raw & {encodedMask.ToString(CultureInfo.InvariantCulture)}UL) != {value.ToString(CultureInfo.InvariantCulture)}UL"));
            }
        }
        switch (encoded.SpecialType)
        {
            case SpecialType.System_Boolean: writer = "TryWriteBoolean"; reader = "TryReadBoolean"; bytes = 1; return true;
            case SpecialType.System_Byte: writer = "TryWriteByte"; reader = "TryReadByte"; bytes = 1; return true;
            case SpecialType.System_SByte: writer = "TryWriteByte"; reader = "TryReadByte"; bytes = 1; conversion ??= "sbyte"; return true;
            case SpecialType.System_Int16: writer = "TryWriteInt16"; reader = "TryReadInt16"; bytes = 2; return true;
            case SpecialType.System_UInt16: writer = "TryWriteUInt16"; reader = "TryReadUInt16"; bytes = 2; return true;
            case SpecialType.System_Int32: writer = "TryWriteInt32"; reader = "TryReadInt32"; bytes = 4; return true;
            case SpecialType.System_UInt32: writer = "TryWriteUInt32"; reader = "TryReadUInt32"; bytes = 4; return true;
            case SpecialType.System_Int64: writer = "TryWriteInt64"; reader = "TryReadInt64"; bytes = 8; return true;
            case SpecialType.System_UInt64: writer = "TryWriteUInt64"; reader = "TryReadUInt64"; bytes = 8; return true;
            default: writer = reader = string.Empty; bytes = 0; return false;
        }
    }

    private static string EmitCodec(Message message)
    {
        string ns = message.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : $"namespace {message.Symbol.ContainingNamespace.ToDisplayString()};\n\n";
        string type = message.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        int size = message.Fields.Sum(static field => field.Bytes);
        ulong fingerprint = Fingerprint(message);
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n").Append(ns)
            .Append("public static class ").Append(message.Symbol.Name).Append("Codec\n{\n")
            .Append("    public const int EncodedSize = ").Append(size).Append(";\n")
            .Append("    public static global::UltraSim.Network.NetworkMessageDescriptor Descriptor => new(\n")
            .Append("        new global::UltraSim.Network.NetworkMessageId(").Append(message.Id).Append("), \"")
            .Append(Escape(message.Key)).Append("\", ").Append(message.Version)
            .Append(", (global::UltraSim.Network.NetworkLane)").Append(message.Lane)
            .Append(", EncodedSize, ").Append(fingerprint).Append("UL);\n\n")
            .Append("    public static bool TryMeasure(in ").Append(type).Append(" value, out int bytes)\n")
            .Append("    {\n");
        AppendWriteValidation(source, message.Fields, "        ", "bytes = 0; return false;");
        source.Append("        bytes = EncodedSize;\n        return true;\n    }\n\n")
            .Append("    public static bool TryWrite(ref global::UltraSim.Network.NetworkPacketWriter writer, in ")
            .Append(type).Append(" value)\n    {\n");
        AppendWriteValidation(source, message.Fields, "        ", "return false;");
        source.Append("        var staged = writer;\n");
        foreach (Field field in message.Fields)
        {
            string value = WriteValue(field);
            source.Append("        if (!staged.").Append(field.Writer).Append('(').Append(value).Append(")) return false;\n");
        }
        source.Append("        writer = staged;\n        return true;\n    }\n\n")
            .Append("    public static bool TryRead(ref global::UltraSim.Network.NetworkPacketReader reader, out ")
            .Append(type).Append(" value)\n    {\n        var staged = reader;\n");
        for (int index = 0; index < message.Fields.Count; index++)
        {
            Field field = message.Fields[index];
            string rawType = ReaderType(field.Reader);
            source.Append("        if (!staged.").Append(field.Reader).Append("(out ").Append(rawType)
                .Append(" raw").Append(index).Append(")) { value = default; return false; }\n");
            if (field.Validation is not null)
                source.Append("        if (").Append(field.Validation.Replace("raw", $"unchecked((ulong)raw{index})"))
                    .Append(") { value = default; return false; }\n");
        }
        source.Append("        value = new ").Append(type).Append("\n        {\n");
        for (int index = 0; index < message.Fields.Count; index++)
        {
            Field field = message.Fields[index];
            source.Append("            ").Append(field.Name).Append(" = ")
                .Append(ReadConversion(field, index)).Append(",\n");
        }
        source.Append("        };\n        reader = staged;\n        return true;\n    }\n\n")
            .Append("    public static bool TryWriteExact(global::System.Span<byte> destination, in ").Append(type)
            .Append(" value)\n    {\n        if (destination.Length != EncodedSize) return false;\n")
            .Append("        var writer = new global::UltraSim.Network.NetworkPacketWriter(destination);\n")
            .Append("        return TryWrite(ref writer, in value) && writer.WrittenCount == EncodedSize;\n    }\n\n")
            .Append("    public static bool TryReadExact(global::System.ReadOnlySpan<byte> source, out ").Append(type)
            .Append(" value)\n    {\n        if (source.Length != EncodedSize) { value = default; return false; }\n")
            .Append("        var reader = new global::UltraSim.Network.NetworkPacketReader(source);\n")
            .Append("        return TryRead(ref reader, out value) && reader.IsComplete;\n    }\n}\n");
        return source.ToString();
    }

    private static string EmitManifest(INamedTypeSymbol protocol, ushort version, IReadOnlyList<Message> messages)
    {
        string ns = protocol.ContainingNamespace.IsGlobalNamespace ? string.Empty : $"namespace {protocol.ContainingNamespace.ToDisplayString()};\n\n";
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n").Append(ns)
            .Append("public static partial class ").Append(protocol.Name).Append("\n{\n")
            .Append("    private static readonly global::UltraSim.Network.NetworkProtocolManifest s_manifest =\n")
            .Append("        global::UltraSim.Network.NetworkProtocolManifest.Create(").Append(version)
            .Append(", new global::UltraSim.Network.NetworkMessageDescriptor[]\n        {\n");
        foreach (Message message in messages)
            source.Append("            ").Append(CodecType(message.Symbol)).Append(".Descriptor,\n");
        source.Append("        });\n\n    public static global::UltraSim.Network.NetworkProtocolManifest Manifest => s_manifest;\n}\n");
        return source.ToString();
    }

    private static string UnderlyingCast(Field field) => field.Writer switch
    {
        "TryWriteByte" => "byte", "TryWriteInt16" => "short", "TryWriteUInt16" => "ushort",
        "TryWriteInt32" => "int", "TryWriteUInt32" => "uint", "TryWriteInt64" => "long",
        "TryWriteUInt64" => "ulong", _ => field.Type,
    };

    private static string WriteValue(Field field)
    {
        string value = field.Conversion is null
            ? $"value.{field.Name}"
            : $"({UnderlyingCast(field)})value.{field.Name}";
        if (field.Writer == "TryWriteByte" &&
            (field.Type == "sbyte" || field.Type == "global::System.SByte" || field.Conversion is not null))
        {
            value = $"unchecked((byte)value.{field.Name})";
        }
        return value;
    }

    private static void AppendWriteValidation(
        StringBuilder source,
        IReadOnlyList<Field> fields,
        string indent,
        string failure)
    {
        foreach (Field field in fields)
        {
            if (field.Validation is null) continue;
            string raw = $"unchecked((ulong)({WriteValue(field)}))";
            source.Append(indent).Append("if (").Append(field.Validation.Replace("raw", raw))
                .Append(") { ").Append(failure).Append(" }\n");
        }
    }

    private static string CodecType(INamedTypeSymbol symbol) => symbol.ContainingNamespace.IsGlobalNamespace
        ? $"global::{symbol.Name}Codec"
        : $"global::{symbol.ContainingNamespace.ToDisplayString()}.{symbol.Name}Codec";

    private static string HintStem(INamedTypeSymbol symbol)
    {
        string identity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var safe = new StringBuilder(identity.Length + 17);
        ulong hash = 14695981039346656037UL;
        foreach (char character in identity)
        {
            hash = unchecked((hash ^ character) * 1099511628211UL);
            if (safe.Length < 120)
                safe.Append(char.IsLetterOrDigit(character) ? character : '_');
        }
        return safe.Append('_').Append(hash.ToString("X16", CultureInfo.InvariantCulture)).ToString();
    }

    private static string ReaderType(string reader) => reader switch
    {
        "TryReadBoolean" => "bool", "TryReadByte" => "byte", "TryReadInt16" => "short",
        "TryReadUInt16" => "ushort", "TryReadInt32" => "int", "TryReadUInt32" => "uint",
        "TryReadInt64" => "long", "TryReadUInt64" => "ulong", _ => throw new InvalidOperationException(),
    };

    private static string ReadConversion(Field field, int index)
    {
        if (field.Type is "sbyte" or "global::System.SByte") return $"unchecked((sbyte)raw{index})";
        if (field.Conversion is not null && field.Reader == "TryReadByte")
            return $"unchecked(({field.Type})(sbyte)raw{index})";
        return field.Conversion is null ? $"raw{index}" : $"({field.Type})raw{index}";
    }

    private static AttributeData? FindAttribute(INamedTypeSymbol symbol, string name) =>
        symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == name);

    private static bool IsPartial(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences
        .Select(reference => reference.GetSyntax()).OfType<TypeDeclarationSyntax>()
        .All(static declaration => declaration.Modifiers.Any(static modifier => modifier.Text == "partial"));

    private static void ReportDuplicate(SourceProductionContext context, Message first, Message second, string identity) =>
        context.ReportDiagnostic(Diagnostic.Create(DuplicateIdentity, second.Symbol.Locations.FirstOrDefault(),
            first.Symbol.Name, second.Symbol.Name, identity));

    private static ulong Fingerprint(Message message)
    {
        ulong hash = 14695981039346656037UL;
        void Mix(string text) { foreach (char value in text) hash = unchecked((hash ^ value) * 1099511628211UL); }
        Mix(message.Id.ToString(CultureInfo.InvariantCulture));
        Mix(message.Key);
        Mix(message.Version.ToString(CultureInfo.InvariantCulture));
        foreach (Field field in message.Fields) { Mix(field.Name); Mix(field.Type); Mix(field.Bytes.ToString(CultureInfo.InvariantCulture)); }
        return hash;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class Message
    {
        internal Message(INamedTypeSymbol symbol, ushort id, string key, ushort version, byte lane, List<Field> fields) =>
            (Symbol, Id, Key, Version, Lane, Fields) = (symbol, id, key, version, lane, fields);
        internal INamedTypeSymbol Symbol { get; }
        internal ushort Id { get; }
        internal string Key { get; }
        internal ushort Version { get; }
        internal byte Lane { get; }
        internal List<Field> Fields { get; }
    }

    private sealed class Field
    {
        internal Field(string name, string type, string writer, string reader, int bytes, string? conversion, string? validation) =>
            (Name, Type, Writer, Reader, Bytes, Conversion, Validation) =
                (name, type, writer, reader, bytes, conversion, validation);
        internal string Name { get; }
        internal string Type { get; }
        internal string Writer { get; }
        internal string Reader { get; }
        internal int Bytes { get; }
        internal string? Conversion { get; }
        internal string? Validation { get; }
    }
}
