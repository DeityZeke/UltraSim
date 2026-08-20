using System;

namespace UltraSim.Server.Engines.Accounting
{
    public sealed class AccountPersistenceSaveSnapshot
    {
        public AccountPersistenceSaveSnapshot(
            AccountModel[] accounts,
            CharacterModel[] characters,
            IpBanEntry[] ipBans)
        {
            Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            Characters = characters ?? throw new ArgumentNullException(nameof(characters));
            IpBans = ipBans ?? throw new ArgumentNullException(nameof(ipBans));
        }

        public AccountModel[] Accounts { get; }
        public CharacterModel[] Characters { get; }
        public IpBanEntry[] IpBans { get; }
    }
}
