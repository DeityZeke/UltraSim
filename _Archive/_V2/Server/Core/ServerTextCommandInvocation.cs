namespace UltraSim.Server;

public sealed class ServerTextCommandInvocation
{
    public ServerTextCommandInvocation(
        string prefix,
        string commandName,
        string rawInput,
        string rawArguments,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> namedArguments)
    {
        Prefix = prefix;
        CommandName = commandName;
        RawInput = rawInput;
        RawArguments = rawArguments;
        Arguments = arguments;
        NamedArguments = namedArguments;
    }

    public string Prefix { get; }
    public string CommandName { get; }
    public string RawInput { get; }
    public string RawArguments { get; }
    public IReadOnlyList<string> Arguments { get; }
    public IReadOnlyDictionary<string, string> NamedArguments { get; }
}
