using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests;

public sealed class ClickControlServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cu-click-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task UnknownControl_IsUnknownControl()
    {
        var env = Create(host: false);
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, "ctl.missing", null, CancellationToken.None));
        Assert.Equal(ErrorCodes.UnknownControl, ex.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task HostWindow_IsHostWindowForbidden()
    {
        var env = Create(host: true);
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, "ctl.x", null, CancellationToken.None));
        Assert.Equal(ErrorCodes.HostWindowForbidden, ex.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task ControlFromScreenA_OnFrameB_IsScreenMismatch()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frameA = ScreenFrame(seed: 3);
        env.Frames.Add(frameA);
        var screenA = remember.RememberScreen(frameA, AppKeyValue, "a", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frameA, AppKeyValue, screenA, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var frameB = ScreenFrame(seed: 91, frameId: "fr1.b", visualized: false);
        env.Frames.Add(frameB);
        env.Capture.Pixels = frameB.Bgra!;
        env.Capture.Width = frameB.Width;
        env.Capture.Height = frameB.Height;

        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        Assert.Equal(ErrorCodes.ScreenMismatch, ex.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task MatchThenClick_UsesHitTestAndActivate()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var screenId = remember.RememberScreen(frame, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frame, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        env.Frames.Add(live);
        env.Capture.Pixels = live.Bgra!;
        env.Capture.Width = live.Width;
        env.Capture.Height = live.Height;

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:down", StringComparison.Ordinal));
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:up", StringComparison.Ordinal));
    }

    private Env Create(bool host)
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = @"c:\apps\app.exe" };
        world.Windows[1] = new FakeWindow
        {
            Hwnd = 1,
            Pid = 1,
            ClassName = "Notepad",
            WindowRect = new ScreenRect(0, 0, 400, 400),
            ExtendedFrameBounds = new ScreenRect(0, 0, 400, 400)
        };
        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 1, 1, "Notepad");
        var frames = new FrameCache(Limits.V1);
        var input = new RecordingInjector();
        var capture = new FakeCapture { Width = 400, Height = 400, Pixels = BgraFrames.Solid(400, 400, 0, 0, 0) };
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var click = new ClickControlService(
            new DesktopOperationCoordinator(Limits.V1),
            new OperationIdCache(Limits.V1),
            tokens,
            frames,
            world,
            new FakeMonitors(),
            world,
            new FakeDesktops(),
            new FakeSession(),
            new FakeActivator { Foreground = 1 },
            new FakeHitTester { Hit = 1 },
            input,
            new StubHost { Result = host },
            catalog,
            Limits.V1);
        return new Env(click, token, frames, catalog, input, capture);
    }

    private static FrameRecord ScreenFrame(int seed, string frameId = "fr1.a", bool visualized = true)
    {
        var bgra = BgraFrames.Solid(400, 400, 20, 20, 20);
        BgraFrames.Paste(bgra, 400, BgraFrames.Checker(32, 32, 2), 32, 32, 8, 8);
        BgraFrames.Paste(bgra, 400, BgraFrames.Noise(32, 32, seed), 32, 32, 320, 320);
        return TestFrames.Create(400, 400, bgra, visualized, frameId);
    }

    private static string AppKeyValue =>
        AppKeyResolver.Compute(new AppIdentity(null, null, null, null, @"c:\apps\app.exe", "Notepad")).Value;

    private static PixelBox[] Spread() => [new(8, 8, 32, 32), new(320, 320, 32, 32)];

    private sealed record Env(
        ClickControlService Click,
        string Token,
        FrameCache Frames,
        MemoryCatalog Catalog,
        RecordingInjector Input,
        FakeCapture Capture);
}
