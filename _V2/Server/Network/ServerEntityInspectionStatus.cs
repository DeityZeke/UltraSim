namespace UltraSim.Server.Network;

public enum ServerEntityInspectionStatus : byte
{
    Success = 0,
    EntityNotFound = 1,
    PermissionDenied = 2,
}
