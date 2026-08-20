namespace UltraSim.Server.Accounting;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);

    bool NeedsRehash(string passwordHash);
}
