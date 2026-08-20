using UltraSim.Portal.Authorization;
using Xunit;

namespace UltraSim.Portal.Tests.Authorization;

public sealed class PortalRoleResolverTests
{
    // CanManageTarget tests

    [Fact]
    public void CanManageTarget_TargetIsPrimaryOwner_ReturnsFalse()
    {
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.PrimaryOwner, PortalRoles.PrimaryOwner, targetIsPrimaryOwner: true));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.PrimaryOwner, targetIsPrimaryOwner: true));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.User, targetIsPrimaryOwner: true));
    }

    [Fact]
    public void CanManageTarget_PrimaryOwnerActor_CanManageAnyone()
    {
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.PrimaryOwner, PortalRoles.Owner, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.PrimaryOwner, PortalRoles.Admin, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.PrimaryOwner, PortalRoles.User, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_OwnerActor_CannotManagePrimaryOwner()
    {
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.PrimaryOwner, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_OwnerActor_CanManageBelowOwner()
    {
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.Admin, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.Moderator, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.User, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_OwnerActor_CanManageOtherOwner()
    {
        // Owner can manage Owner (not PrimaryOwner restriction)
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Owner, PortalRoles.Owner, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_AdminActor_CannotManageAdminOrAbove()
    {
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.PrimaryOwner, targetIsPrimaryOwner: false));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.Owner, targetIsPrimaryOwner: false));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.Admin, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_AdminActor_CanManageBelowAdmin()
    {
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.Moderator, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.Developer, targetIsPrimaryOwner: false));
        Assert.True(PortalRoleResolver.CanManageTarget(PortalRoles.Admin, PortalRoles.User, targetIsPrimaryOwner: false));
    }

    [Fact]
    public void CanManageTarget_UserActor_CannotManageAnyone()
    {
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.User, PortalRoles.User, targetIsPrimaryOwner: false));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.User, PortalRoles.Moderator, targetIsPrimaryOwner: false));
        Assert.False(PortalRoleResolver.CanManageTarget(PortalRoles.Moderator, PortalRoles.User, targetIsPrimaryOwner: false));
    }

    // GetAssignableRolesFor tests

    [Fact]
    public void GetAssignableRolesFor_PrimaryOwner_ReturnsOwnerAssignableSet()
    {
        string[] roles = PortalRoleResolver.GetAssignableRolesFor(PortalRoles.PrimaryOwner);
        Assert.Contains(PortalRoles.Owner, roles);
        Assert.Contains(PortalRoles.Admin, roles);
        Assert.DoesNotContain(PortalRoles.PrimaryOwner, roles);
    }

    [Fact]
    public void GetAssignableRolesFor_Owner_SameAsOwnerAssignableRoles()
    {
        string[] roles = PortalRoleResolver.GetAssignableRolesFor(PortalRoles.Owner);
        Assert.Contains(PortalRoles.Owner, roles);
        Assert.Contains(PortalRoles.Admin, roles);
        Assert.DoesNotContain(PortalRoles.PrimaryOwner, roles);
    }

    [Fact]
    public void GetAssignableRolesFor_Admin_CannotAssignOwnerOrAbove()
    {
        string[] roles = PortalRoleResolver.GetAssignableRolesFor(PortalRoles.Admin);
        Assert.DoesNotContain(PortalRoles.Owner, roles);
        Assert.DoesNotContain(PortalRoles.PrimaryOwner, roles);
        Assert.Contains(PortalRoles.Moderator, roles);
        Assert.Contains(PortalRoles.Developer, roles);
    }

    [Fact]
    public void GetAssignableRolesFor_User_ReturnsOnlyUser()
    {
        string[] roles = PortalRoleResolver.GetAssignableRolesFor(PortalRoles.User);
        Assert.Single(roles);
        Assert.Equal(PortalRoles.User, roles[0]);
    }

    [Fact]
    public void GetAssignableRolesFor_Moderator_ReturnsOnlyUser()
    {
        // Moderator has no special assignment power - falls through to default
        string[] roles = PortalRoleResolver.GetAssignableRolesFor(PortalRoles.Moderator);
        Assert.Single(roles);
        Assert.Equal(PortalRoles.User, roles[0]);
    }

    [Fact]
    public void GetAssignableRolesFor_UnknownRole_ReturnsOnlyUser()
    {
        string[] roles = PortalRoleResolver.GetAssignableRolesFor("NoSuchRole");
        Assert.Single(roles);
        Assert.Equal(PortalRoles.User, roles[0]);
    }
}
