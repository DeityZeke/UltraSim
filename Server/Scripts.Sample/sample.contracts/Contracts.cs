namespace Sample.Contracts;

public interface IGreeting
{
    string Name { get; }
}

public static class SampleIdentity
{
    public const string Greeting = "sample.greeting";
}
