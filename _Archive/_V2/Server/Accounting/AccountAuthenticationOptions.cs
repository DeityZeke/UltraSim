namespace UltraSim.Server.Accounting;

public sealed record AccountAuthenticationOptions(
    AccountAuthenticationMode Mode = AccountAuthenticationMode.LocalServerAccounts,
    string ServerInstanceId = "local",
    AccountSessionConflictMode SessionConflictMode = AccountSessionConflictMode.DenySecondLogin);
