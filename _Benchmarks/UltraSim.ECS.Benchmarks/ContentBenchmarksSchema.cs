using System.Runtime.InteropServices;
using UltraSim.ECS;

namespace UltraSim.ECS.Benchmarks;

[EcsComponent("benchmark.content.payload16")]
[StructLayout(LayoutKind.Sequential, Size = 16)]
internal struct ContentPayload16 : IComponent<ContentPayload16> { public long Value { get; set; } }

[EcsComponent("benchmark.content.payload64")]
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct ContentPayload64 : IComponent<ContentPayload64> { public long Value { get; set; } }

[EcsComponent("benchmark.content.payload256")]
[StructLayout(LayoutKind.Sequential, Size = 256)]
internal struct ContentPayload256 : IComponent<ContentPayload256> { public long Value { get; set; } }

[EcsComponent("benchmark.content.payload1024")]
[StructLayout(LayoutKind.Sequential, Size = 1_024)]
internal struct ContentPayload1024 : IComponent<ContentPayload1024> { public long Value { get; set; } }

[EcsComponent("benchmark.content.tag0", IsTag = true)]
internal struct ContentTag0 : IComponent<ContentTag0> { }
[EcsComponent("benchmark.content.tag1", IsTag = true)]
internal struct ContentTag1 : IComponent<ContentTag1> { }
[EcsComponent("benchmark.content.tag2", IsTag = true)]
internal struct ContentTag2 : IComponent<ContentTag2> { }
[EcsComponent("benchmark.content.tag3", IsTag = true)]
internal struct ContentTag3 : IComponent<ContentTag3> { }
[EcsComponent("benchmark.content.tag4", IsTag = true)]
internal struct ContentTag4 : IComponent<ContentTag4> { }
[EcsComponent("benchmark.content.tag5", IsTag = true)]
internal struct ContentTag5 : IComponent<ContentTag5> { }
[EcsComponent("benchmark.content.tag6", IsTag = true)]
internal struct ContentTag6 : IComponent<ContentTag6> { }

[EcsSchema(
    typeof(ContentPayload16), typeof(ContentPayload64), typeof(ContentPayload256),
    typeof(ContentPayload1024), typeof(ContentTag0), typeof(ContentTag1),
    typeof(ContentTag2), typeof(ContentTag3), typeof(ContentTag4),
    typeof(ContentTag5), typeof(ContentTag6))]
internal static partial class ContentBenchmarkSchema { }
