using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UltraSim.Network;
using UltraSim.Network.Generators;

var tests = new (string Name, Action Body)[]
{
    ("ValidProtocolGeneratesExactCodecAndManifest", ValidProtocolGeneratesExactCodecAndManifest),
    ("SignedValuesRoundTripUnderCheckedOverflow", SignedValuesRoundTripUnderCheckedOverflow),
    ("EnumWritesRejectNonCanonicalValuesTransactionally", EnumWritesRejectNonCanonicalValuesTransactionally),
    ("CrossNamespaceSameNamedMessagesGenerateUniqueCodecs", CrossNamespaceSameNamedMessagesGenerateUniqueCodecs),
    ("UnsupportedDeclarationShapesReportNETGEN001", UnsupportedDeclarationShapesReport),
    ("DuplicateMessageIdReportsNETGEN003", DuplicateMessageIdReports),
    ("DuplicateCanonicalKeyReportsNETGEN003", DuplicateCanonicalKeyReports),
    ("UnsupportedFieldReportsNETGEN002", UnsupportedFieldReports),
};
foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

static void SignedValuesRoundTripUnderCheckedOverflow()
{
    Generation generation = Generate("""
        using System;
        using UltraSim.Network;
        namespace SignedFixture;
        public enum SByteMode : sbyte { Negative = -1, Positive = 1 }
        public enum ShortMode : short { Negative = -2, Positive = 2 }
        public enum IntMode : int { Negative = -3, Positive = 3 }
        public enum LongMode : long { Negative = -4, Positive = 4 }
        [NetworkProtocol(1)] public static partial class Protocol { }
        [NetworkMessage(1, "signed", 1, NetworkLane.Control)]
        public partial struct SignedMessage
        {
            public sbyte Byte;
            public SByteMode SByteMode;
            public ShortMode ShortMode;
            public IntMode IntMode;
            public LongMode LongMode;
        }
        public static class Probe
        {
            public static bool Run()
            {
                var value = new SignedMessage
                {
                    Byte = sbyte.MinValue,
                    SByteMode = SByteMode.Negative,
                    ShortMode = ShortMode.Negative,
                    IntMode = IntMode.Negative,
                    LongMode = LongMode.Negative,
                };
                Span<byte> bytes = stackalloc byte[SignedMessageCodec.EncodedSize];
                return SignedMessageCodec.TryWriteExact(bytes, in value) &&
                    SignedMessageCodec.TryReadExact(bytes, out SignedMessage decoded) &&
                    decoded.Byte == value.Byte && decoded.SByteMode == value.SByteMode &&
                    decoded.ShortMode == value.ShortMode && decoded.IntMode == value.IntMode &&
                    decoded.LongMode == value.LongMode;
            }
        }
        """);
    AssertNoGeneratorErrors(generation);
    Diagnostic[] errors = generation.Compilation.GetDiagnostics()
        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.AsEnumerable()));
    using var stream = new MemoryStream();
    Microsoft.CodeAnalysis.Emit.EmitResult emitted = generation.Compilation.Emit(stream);
    if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
    System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(stream.ToArray());
    bool passed = (bool)assembly.GetType("SignedFixture.Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    if (!passed) throw new InvalidOperationException("The signed checked-build codec did not round-trip.");
}

static void EnumWritesRejectNonCanonicalValuesTransactionally()
{
    Generation generation = Generate("""
        using System;
        using UltraSim.Network;
        namespace EnumFixture;
        public enum Mode : byte { A = 1, B = 2 }
        [Flags] public enum Options : byte { None = 0, A = 1, B = 2 }
        [NetworkProtocol(1)] public static partial class Protocol { }
        [NetworkMessage(1, "enum", 1, NetworkLane.Control)]
        public partial struct EnumMessage { public Mode Mode; public Options Options; }
        public static class Probe
        {
            public static bool Run()
            {
                var badMode = new EnumMessage { Mode = (Mode)3, Options = Options.A };
                var badFlags = new EnumMessage { Mode = Mode.A, Options = (Options)4 };
                Span<byte> bytes = stackalloc byte[16];
                var writer = new NetworkPacketWriter(bytes);
                if (!writer.TryWriteByte(99)) return false;
                int before = writer.WrittenCount;
                return !EnumMessageCodec.TryMeasure(in badMode, out int badModeBytes) && badModeBytes == 0 &&
                    !EnumMessageCodec.TryWrite(ref writer, in badMode) && writer.WrittenCount == before &&
                    !EnumMessageCodec.TryMeasure(in badFlags, out int badFlagsBytes) && badFlagsBytes == 0 &&
                    !EnumMessageCodec.TryWrite(ref writer, in badFlags) && writer.WrittenCount == before;
            }
        }
        """);
    AssertExecutableProbe(generation, "EnumFixture.Probe");
}

static void CrossNamespaceSameNamedMessagesGenerateUniqueCodecs()
{
    Generation generation = Generate("""
        using UltraSim.Network;
        namespace ProtocolSpace { [NetworkProtocol(1)] public static partial class Protocol { } }
        namespace First { [NetworkMessage(1, "first", 1, NetworkLane.Control)] public partial struct Same { public uint Value; } }
        namespace Second { [NetworkMessage(2, "second", 1, NetworkLane.Event)] public partial struct Same { public uint Value; } }
        """);
    AssertNoCompilationErrors(generation);
    Contains(generation.Source, "global::First.SameCodec.Descriptor");
    Contains(generation.Source, "global::Second.SameCodec.Descriptor");
}

static void UnsupportedDeclarationShapesReport()
{
    AssertDiagnostic("""
        using UltraSim.Network;
        [NetworkProtocol(1)] internal static partial class Protocol { }
        [NetworkMessage(1, "a", 1, NetworkLane.Control)] public partial struct A { public uint Value; }
        """, "NETGEN001");
    AssertDiagnostic("""
        using UltraSim.Network;
        [NetworkProtocol(1)] public static partial class Protocol<T> { }
        [NetworkMessage(1, "a", 1, NetworkLane.Control)] public partial struct A { public uint Value; }
        """, "NETGEN001");
    AssertDiagnostic("""
        using UltraSim.Network;
        [NetworkProtocol(1)] public static partial class Protocol { }
        [NetworkMessage(1, "a", 1, NetworkLane.Control)] internal partial struct A { public uint Value; }
        """, "NETGEN001");
    AssertDiagnostic("""
        using UltraSim.Network;
        [NetworkProtocol(1)] public static partial class Protocol { }
        [NetworkMessage(1, "a", 1, NetworkLane.Control)] public partial struct A<T> { public uint Value; }
        """, "NETGEN001");
}
Console.WriteLine($"All {tests.Length} Network codec generator tests passed.");

static void ValidProtocolGeneratesExactCodecAndManifest()
{
    Generation generation = Generate("""
        using UltraSim.Network;
        namespace Fixture;
        [System.Flags] public enum Options : byte { None = 0, A = 1, B = 2 }
        [NetworkProtocol(3)] public static partial class Protocol { }
        [NetworkMessage(7, "fixture.sample", 2, NetworkLane.Input)]
        public partial struct Sample
        {
            public ulong Sequence;
            public int Delta;
            public bool Active;
            public Options Flags;
        }
        """);
    AssertNoGeneratorErrors(generation);
    Diagnostic[] errors = generation.Compilation.GetDiagnostics()
        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.AsEnumerable()));
    Contains(generation.Source, "public const int EncodedSize = 14;");
    Contains(generation.Source, "TryWriteExact");
    Contains(generation.Source, "TryReadExact");
    Contains(generation.Source, "NetworkProtocolManifest.Create(3");
}

static void DuplicateMessageIdReports() => AssertDiagnostic("""
    using UltraSim.Network;
    [NetworkProtocol(1)] public static partial class Protocol { }
    [NetworkMessage(1, "a", 1, NetworkLane.Control)] public partial struct A { public uint Value; }
    [NetworkMessage(1, "b", 1, NetworkLane.Control)] public partial struct B { public uint Value; }
    """, "NETGEN003");

static void DuplicateCanonicalKeyReports() => AssertDiagnostic("""
    using UltraSim.Network;
    [NetworkProtocol(1)] public static partial class Protocol { }
    [NetworkMessage(1, "same", 1, NetworkLane.Control)] public partial struct A { public uint Value; }
    [NetworkMessage(2, "same", 1, NetworkLane.Control)] public partial struct B { public uint Value; }
    """, "NETGEN003");

static void UnsupportedFieldReports() => AssertDiagnostic("""
    using UltraSim.Network;
    [NetworkProtocol(1)] public static partial class Protocol { }
    [NetworkMessage(1, "bad", 1, NetworkLane.Control)] public partial struct Bad { public string Value; }
    """, "NETGEN002");

static void AssertDiagnostic(string source, string id)
{
    Generation generation = Generate(source);
    if (!generation.Diagnostics.Any(diagnostic => diagnostic.Id == id))
        throw new InvalidOperationException($"Expected {id}; got {string.Join(", ", generation.Diagnostics.Select(static d => d.Id))}.");
}

static Generation Generate(string source)
{
    CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
    SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(static path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
    references.Add(MetadataReference.CreateFromFile(typeof(NetworkProtocolAttribute).Assembly.Location));
    CSharpCompilation compilation = CSharpCompilation.Create(
        "NetworkGeneratorFixture", new[] { tree }, references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true));
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        generators: new[] { new NetworkCodecGenerator().AsSourceGenerator() },
        parseOptions: parseOptions);
    driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
    GeneratorDriverRunResult result = driver.GetRunResult();
    string generated = string.Join("\n", result.GeneratedTrees.Select(static generatedTree => generatedTree.ToString()));
    return new Generation(output, result.Diagnostics, generated);
}

static void AssertNoGeneratorErrors(Generation generation)
{
    Diagnostic[] errors = generation.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.AsEnumerable()));
}

static void AssertNoCompilationErrors(Generation generation)
{
    AssertNoGeneratorErrors(generation);
    Diagnostic[] errors = generation.Compilation.GetDiagnostics()
        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.AsEnumerable()));
}

static void AssertExecutableProbe(Generation generation, string typeName)
{
    AssertNoCompilationErrors(generation);
    using var stream = new MemoryStream();
    Microsoft.CodeAnalysis.Emit.EmitResult emitted = generation.Compilation.Emit(stream);
    if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
    System.Reflection.Assembly assembly = System.Reflection.Assembly.Load(stream.ToArray());
    bool passed = (bool)assembly.GetType(typeName)!.GetMethod("Run")!.Invoke(null, null)!;
    if (!passed) throw new InvalidOperationException($"Generated probe '{typeName}' failed.");
}

static void Contains(string source, string value)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException($"Generated source did not contain '{value}'.\n{source}");
}

internal readonly record struct Generation(Compilation Compilation, IReadOnlyList<Diagnostic> Diagnostics, string Source);
