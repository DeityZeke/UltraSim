namespace UltraSim.Server.Accounting;

public readonly record struct Pbkdf2PasswordHashEnvelope(
    int Version,
    int Iterations,
    int SaltLength,
    int HashLength);
