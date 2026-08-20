using UltraSim.Portal.Authorization;
using Xunit;

namespace UltraSim.Portal.Tests.Authorization;

public sealed class PortalRolesTests
{
    [Theory]
    [InlineData(PortalRoles.PrimaryOwner, 100)]
    [InlineData(PortalRoles.Owner, 95)]
    [InlineData(PortalRoles.Admin, 90)]
    [InlineData(PortalRoles.Developer, 80)]
    [InlineData(PortalRoles.Moderator, 40)]
    [InlineData(PortalRoles.User, 0)]
    [InlineData("unknown", 0)]
    [InlineData(null, 0)]
    public void GetTier_ReturnsExpectedTier(string? role, int expectedTier)
    {
        Assert.Equal(expectedTier, PortalRoles.GetTier(role));
    }

    [Fact]
    public void ResolveEffectiveRole_IsPrimaryOwner_ReturnsPrimaryOwner()
    {
        string result = PortalRoles.ResolveEffectiveRole([], isPrimaryOwner: true);
        Assert.Equal(PortalRoles.PrimaryOwner, result);
    }

    [Fact]
    public void ResolveEffectiveRole_IsPrimaryOwner_IgnoresOtherRoles()
    {
        string result = PortalRoles.ResolveEffectiveRole([PortalRoles.User], isPrimaryOwner: true);
        Assert.Equal(PortalRoles.PrimaryOwner, result);
    }

    [Fact]
    public void ResolveEffectiveRole_NoRoles_ReturnsUser()
    {
        string result = PortalRoles.ResolveEffectiveRole([], isPrimaryOwner: false);
        Assert.Equal(PortalRoles.User, result);
    }

    [Fact]
    public void ResolveEffectiveRole_SingleRole_ReturnsThatRole()
    {
        string result = PortalRoles.ResolveEffectiveRole([PortalRoles.Admin], isPrimaryOwner: false);
        Assert.Equal(PortalRoles.Admin, result);
    }

    [Fact]
    public void ResolveEffectiveRole_MultipleRoles_ReturnsHighestTier()
    {
        string result = PortalRoles.ResolveEffectiveRole(
            [PortalRoles.Moderator, PortalRoles.Admin, PortalRoles.Developer],
            isPrimaryOwner: false);
        Assert.Equal(PortalRoles.Admin, result);
    }

    [Fact]
    public void ResolveEffectiveRole_UnknownRole_ReturnsUser()
    {
        string result = PortalRoles.ResolveEffectiveRole(["SomeRandomRole"], isPrimaryOwner: false);
        Assert.Equal(PortalRoles.User, result);
    }

    [Fact]
    public void All_DoesNotContainUser()
    {
        // User is not a real assigned role - it is a default label for users with no roles
        Assert.DoesNotContain(PortalRoles.User, PortalRoles.All);
    }

    [Fact]
    public void All_ContainsAllNamedRoles()
    {
        Assert.Contains(PortalRoles.PrimaryOwner, PortalRoles.All);
        Assert.Contains(PortalRoles.Owner, PortalRoles.All);
        Assert.Contains(PortalRoles.Admin, PortalRoles.All);
        Assert.Contains(PortalRoles.Developer, PortalRoles.All);
        Assert.Contains(PortalRoles.Moderator, PortalRoles.All);
    }

    [Fact]
    public void AdminAssignableRoles_DoesNotContainOwnerOrPrimaryOwner()
    {
        Assert.DoesNotContain(PortalRoles.Owner, PortalRoles.AdminAssignableRoles);
        Assert.DoesNotContain(PortalRoles.PrimaryOwner, PortalRoles.AdminAssignableRoles);
    }

    [Fact]
    public void OwnerAssignableRoles_DoesNotContainPrimaryOwner()
    {
        Assert.DoesNotContain(PortalRoles.PrimaryOwner, PortalRoles.OwnerAssignableRoles);
    }

    [Fact]
    public void OwnerAssignableRoles_ContainsOwner()
    {
        Assert.Contains(PortalRoles.Owner, PortalRoles.OwnerAssignableRoles);
    }

    [Fact]
    public void AdminPanelRoles_ContainsPrimaryOwnerOwnerAdmin()
    {
        Assert.Contains(PortalRoles.PrimaryOwner, PortalRoles.AdminPanelRoles);
        Assert.Contains(PortalRoles.Owner, PortalRoles.AdminPanelRoles);
        Assert.Contains(PortalRoles.Admin, PortalRoles.AdminPanelRoles);
    }
}
