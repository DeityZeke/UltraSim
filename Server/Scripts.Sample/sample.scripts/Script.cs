using Sample.Contracts;
using UltraSim.Framework.RuntimeHost;

namespace Sample;

public sealed class ScriptEntry : IRuntimePackageLifecycle
{
    private readonly Consumer _consumer = new();
    public void Register(RuntimePackageContext context) =>
        context.Provide(new RuntimeServiceId("sample.consumer"), _consumer);
    public void Configure(RuntimePackageContext context)
    {
        _consumer.ContractsType = typeof(IGreeting).FullName!;
        _consumer.ContractsAssembly = typeof(IGreeting).Assembly.FullName!;
        _consumer.HasCore = context.TryGet<object>(new RuntimeServiceId(SampleIdentity.Greeting), out _);
    }
    public void Initialize(RuntimePackageContext context) { }
    public void Stop() { }
}

public sealed class Consumer
{
    public bool HasCore { get; set; }
    public string ContractsType { get; set; } = "";
    public string ContractsAssembly { get; set; } = "";
}
