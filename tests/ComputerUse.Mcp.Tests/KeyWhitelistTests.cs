using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests;

public sealed class KeyWhitelistTests
{
    [Theory]
    [InlineData("Enter", true)]
    [InlineData("Tab", true)]
    [InlineData("A", true)]
    [InlineData("Z", true)]
    [InlineData("0", true)]
    [InlineData("9", true)]
    [InlineData("F12", true)]
    [InlineData("a", false)]
    [InlineData("Win", false)]
    [InlineData("Enterx", false)]
    [InlineData("F13", false)]
    public void AllowedKeys(string key, bool allowed) => Assert.Equal(allowed, KeyWhitelist.IsAllowedKey(key));

    [Fact]
    public void AltTab_IsForbidden() =>
        Assert.True(KeyWhitelist.IsForbiddenCombo("Tab", ["Alt"]));

    [Fact]
    public void CtrlShiftEsc_IsForbidden() =>
        Assert.True(KeyWhitelist.IsForbiddenCombo("Escape", ["Ctrl", "Shift"]));

    [Fact]
    public void CtrlAltDel_IsForbidden() =>
        Assert.True(KeyWhitelist.IsForbiddenCombo("Delete", ["Ctrl", "Alt"]));

    [Fact]
    public void AltF4_IsTerminator() =>
        Assert.True(KeyWhitelist.IsAltF4("F4", ["Alt"]));

    [Fact]
    public void CtrlS_IsAllowedCombo() =>
        Assert.False(KeyWhitelist.IsForbiddenCombo("S", ["Ctrl"]));
}
