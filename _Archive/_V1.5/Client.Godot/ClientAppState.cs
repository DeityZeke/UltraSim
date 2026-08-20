namespace Client.Godot
{
    public enum ClientAppState : byte
    {
        Boot = 0,
        Preview = 1,
        Connecting = 2,
        /// <summary>TCP session established; waiting for the server to grant in-game access.</summary>
        Connected = 3,
        InGame = 4,
        Disconnected = 5,
        Error = 6,
    }

    public enum NetworkDriverConnectionState : byte
    {
        Idle = 0,
        Connecting = 1,
        Connected = 2,
        Disconnected = 3,
        Error = 4,
    }
}
