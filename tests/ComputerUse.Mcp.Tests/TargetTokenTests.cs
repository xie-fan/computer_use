using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Tests.Fakes;

namespace ComputerUse.Mcp.Tests;

public sealed class TargetTokenTests
{
    [Fact]
    public void Revalidate_HwndReuse_WithDifferentPid_IsStaleTarget()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000 };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, ClassName = "Notepad" };

        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 10, 1000, "Notepad");
        var original = tokens.RequireValid(token, world, world);
        Assert.Equal(10u, original.Pid);

        world.Windows[1].Pid = 11;
        world.Processes[11] = new FakeProcess { Pid = 11, CreateTimeUtc = 2000 };

        var ex = Assert.Throws<ComputerUseException>(() => tokens.RequireValid(token, world, world));
        Assert.Equal(ErrorCodes.StaleTarget, ex.Code);
    }

    [Fact]
    public void Revalidate_HwndReuse_WithDifferentCreateTime_IsStaleTarget()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000 };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, ClassName = "Notepad" };
        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 10, 1000, "Notepad");

        world.Processes[10].CreateTimeUtc = 9999;

        var ex = Assert.Throws<ComputerUseException>(() => tokens.RequireValid(token, world, world));
        Assert.Equal(ErrorCodes.StaleTarget, ex.Code);
    }

    [Fact]
    public void Revalidate_ClassNameChange_IsStaleTarget()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000 };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, ClassName = "Old" };
        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 10, 1000, "Old");
        world.Windows[1].ClassName = "New";
        var ex = Assert.Throws<ComputerUseException>(() => tokens.RequireValid(token, world, world));
        Assert.Equal(ErrorCodes.StaleTarget, ex.Code);
    }

    [Fact]
    public void ForgedToken_IsStaleTarget()
    {
        var world = new FakeWorld();
        world.Processes[10] = new FakeProcess { Pid = 10, CreateTimeUtc = 1000 };
        world.Windows[1] = new FakeWindow { Hwnd = 1, Pid = 10, ClassName = "Notepad" };
        var tokens = new TargetTokenService();
        var ex = Assert.Throws<ComputerUseException>(() => tokens.RequireValid("cu1.not-a-token.abcd", world, world));
        Assert.Equal(ErrorCodes.StaleTarget, ex.Code);
    }

    [Fact]
    public void FormatHwnd_PadsToPointerWidth()
    {
        Assert.Equal("0x00000000000000ff", TargetTokenService.FormatHwnd(0xFF));
    }
}
