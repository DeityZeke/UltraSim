namespace UltraSim.Server;

public delegate void ServerTextCommandHandler(
    in ServerCommandRequest request,
    ServerTextCommandInvocation invocation);
