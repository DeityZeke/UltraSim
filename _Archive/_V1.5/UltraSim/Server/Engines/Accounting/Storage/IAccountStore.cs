using System.Threading.Tasks;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Persistence abstraction for the account store.
    /// Implementations handle format (binary/JSON) and encryption internally.
    /// The caller passes immutable snapshots — thread safety is guaranteed by the
    /// sealed-record + volatile-swap pattern in ServerAccountRegistry.
    /// </summary>
    public interface IAccountStore
    {
        /// <summary>
        /// Loads all characters synchronously on startup.
        /// Called before Load(accounts) so accounts can reference valid character IDs.
        /// Returns an empty array if the store does not yet exist.
        /// </summary>
        CharacterModel[] LoadCharacters();

        /// <summary>
        /// Loads all accounts synchronously on startup.
        /// Returns an empty array if the store does not yet exist.
        /// </summary>
        AccountModel[] Load(CharacterModel[] characters);

        /// <summary>
        /// Loads all IP bans synchronously on startup.
        /// Returns an empty array if the store does not yet exist.
        /// </summary>
        IpBanEntry[] LoadIpBans();

        /// <summary>
        /// Writes a snapshot of all accounts, characters, and IP bans asynchronously.
        /// All arrays are immutable (sealed records); safe to read from any thread.
        /// </summary>
        Task WriteAsync(AccountModel[] accounts, CharacterModel[] characters, IpBanEntry[] ipBans);
    }
}
