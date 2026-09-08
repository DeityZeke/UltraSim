using System.Reflection;
using System.Text.Json;
using UltraSim.Network;

if (args.Length != 1)
    throw new ArgumentException("Usage: UltraSim.NetworkFlavorProbe <evidence-path>");

Assembly assembly = typeof(NetworkKernel).Assembly;
string flavor = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(static attribute =>
        string.Equals(attribute.Key, "UltraSim.Network.Flavor", StringComparison.Ordinal))
    .Value ?? throw new InvalidOperationException("Network flavor metadata has no value.");

var probe = new ProtocolProbeMessage
{
    Nonce = 0x0102_0304_0506_0708UL,
    CapabilityHash = 0x1112_1314U,
    RequestedVersion = 0x2122,
    RequestManifest = true,
    PreferredLane = NetworkLane.Control,
    SignedDelta = -7,
    SignedMode = SignedProtocolProbe.Negative,
};
byte[] packet = new byte[ProtocolProbeMessageCodec.EncodedSize];
if (!ProtocolProbeMessageCodec.TryWriteExact(packet, in probe))
    throw new InvalidOperationException("The canonical Network flavor probe did not encode.");

string[] allTypes = assembly.GetTypes()
    .Select(static type => type.FullName ?? type.Name)
    .OrderBy(static name => name, StringComparer.Ordinal)
    .ToArray();
string[] publicApi = assembly.GetExportedTypes()
    .SelectMany(DescribePublicType)
    .OrderBy(static item => item, StringComparer.Ordinal)
    .ToArray();

var evidence = new NetworkFlavorEvidence(
    assembly.GetName().Name ?? "",
    flavor,
    new FileInfo(assembly.Location).Length,
    CoreNetworkProtocol.Manifest.Version,
    CoreNetworkProtocol.Manifest.Fingerprint,
    Convert.ToHexString(packet),
    allTypes,
    publicApi);

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(
    evidence, new JsonSerializerOptions { WriteIndented = true }));

static IEnumerable<string> DescribePublicType(Type type)
{
    string name = FormatType(type);
    string kind = type.IsEnum ? "enum" :
        type.IsValueType ? "struct" :
        type.IsInterface ? "interface" :
        type.IsClass ? "class" : "type";
    string baseType = type.BaseType is null ? "" : FormatType(type.BaseType);
    string interfaces = string.Join(",", type.GetInterfaces()
        .Select(FormatType).OrderBy(static value => value, StringComparer.Ordinal));
    yield return $"T|{name}|{kind}|{baseType}|{interfaces}|{type.Attributes}";

    const BindingFlags flags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;
    foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        yield return $"C|{name}|{DescribeParameters(constructor.GetParameters())}";
    foreach (MethodInfo method in type.GetMethods(flags))
        yield return $"M|{name}|{method.Name}|{method.IsStatic}|{method.GetGenericArguments().Length}|" +
            $"{FormatType(method.ReturnType)}|{DescribeParameters(method.GetParameters())}";
    foreach (FieldInfo field in type.GetFields(flags))
    {
        string literal = field.IsLiteral
            ? Convert.ToString(field.GetRawConstantValue(),
                System.Globalization.CultureInfo.InvariantCulture) ?? ""
            : "";
        yield return $"F|{name}|{field.Name}|{field.IsStatic}|{FormatType(field.FieldType)}|{literal}";
    }
    foreach (PropertyInfo property in type.GetProperties(flags))
        yield return $"P|{name}|{property.Name}|{FormatType(property.PropertyType)}|" +
            DescribeParameters(property.GetIndexParameters());
    foreach (EventInfo @event in type.GetEvents(flags))
        yield return $"E|{name}|{@event.Name}|{FormatType(@event.EventHandlerType!)}";
}

static string DescribeParameters(ParameterInfo[] parameters) => string.Join(",", parameters.Select(
    static parameter => $"{(parameter.IsOut ? "out:" : parameter.ParameterType.IsByRef ? "ref:" : "")}" +
        FormatType(parameter.ParameterType)));

static string FormatType(Type type)
{
    if (type.IsByRef) return FormatType(type.GetElementType()!) + "&";
    if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
    if (type.IsArray) return FormatType(type.GetElementType()!) + $"[{new string(',', type.GetArrayRank() - 1)}]";
    if (type.IsGenericParameter) return "!" + type.GenericParameterPosition;
    if (!type.IsGenericType) return type.FullName ?? type.Name;
    string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
    int tick = definition.IndexOf('`');
    if (tick >= 0) definition = definition[..tick];
    return definition + "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
}

internal sealed record NetworkFlavorEvidence(
    string AssemblyName,
    string Flavor,
    long AssemblyBytes,
    ushort ProtocolVersion,
    ulong ProtocolFingerprint,
    string CanonicalPacketHex,
    string[] AllTypes,
    string[] PublicApi);
