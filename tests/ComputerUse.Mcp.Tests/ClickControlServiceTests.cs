using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Mcp;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;
using ComputerUse.Mcp.Tests.Support;
using System.Text.Json;

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
    public async Task HostWindow_WithNoImagePath_IsStillHostWindowForbidden()
    {
        var env = Create(host: true, imagePath: null);
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, "ctl.x", null, CancellationToken.None));
        Assert.Equal(ErrorCodes.HostWindowForbidden, ex.Code);
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

        var clicked = await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:down", StringComparison.Ordinal));
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:up", StringComparison.Ordinal));
        var json = ToolResults.SerializeStructured(clicked).GetRawText();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(controlId, doc.RootElement.GetProperty("controlId").GetString());
        Assert.Equal(screenId, doc.RootElement.GetProperty("screenId").GetString());
        Assert.True(doc.RootElement.TryGetProperty("match", out _));
    }

    [Fact]
    public async Task ObserveFrameScreenA_LiveCaptureScreenB_IsScreenMismatch()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frameA = ScreenFrame(seed: 3);
        env.Frames.Add(frameA);
        var screenA = remember.RememberScreen(frameA, AppKeyValue, "a", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frameA, AppKeyValue, screenA, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var frameB = ScreenFrame(seed: 91, frameId: "fr1.b", visualized: false);
        PrimeCapture(env, frameB);

        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        Assert.Equal(ErrorCodes.ScreenMismatch, ex.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task NoCachedFrame_StillCapturesAndClicks()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var remembered = ScreenFrame(seed: 3);
        var screenId = remember.RememberScreen(remembered, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(remembered, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        PrimeCapture(env, live);

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:down", StringComparison.Ordinal));
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CursorMismatch_IsInputPositionMismatch()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var screenId = remember.RememberScreen(frame, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frame, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        PrimeCapture(env, live);
        env.Input.CursorOverride = new ScreenPoint(9999, 9999);

        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        Assert.Equal(ErrorCodes.InputPositionMismatch, ex.Code);
        Assert.Contains(env.Input.Log, line => line.StartsWith("move:", StringComparison.Ordinal));
        Assert.DoesNotContain(env.Input.Log, line => line.StartsWith("mouse:Left:down", StringComparison.Ordinal));
        Assert.DoesNotContain(env.Input.Log, line => line.StartsWith("mouse:Left:up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoPathlessWindows_SameClass_IsAppIdentityUnavailable()
    {
        var env = CreateTwoPathless("Chrome_WidgetWin_1");
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var degenerate = AppKeyResolver.Compute(new AppIdentity(null, null, null, null, null, "Chrome_WidgetWin_1")).Value;
        Assert.Equal("|Chrome_WidgetWin_1", degenerate);
        var screenId = remember.RememberScreen(frame, degenerate, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frame, degenerate, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        env.Frames.Add(live);

        var first = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        var second = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.SecondToken!, controlId, null, CancellationToken.None));
        Assert.Equal(ErrorCodes.AppIdentityUnavailable, first.Code);
        Assert.Equal(ErrorCodes.AppIdentityUnavailable, second.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task DegenerateClassNameLibrary_IsNotLoadedWhenPathMissing()
    {
        var env = Create(host: false, imagePath: null);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var degenerate = AppKeyResolver.Compute(new AppIdentity(null, null, null, null, null, "Notepad")).Value;
        var screenId = remember.RememberScreen(frame, degenerate, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frame, degenerate, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        env.Frames.Add(live);

        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        Assert.Equal(ErrorCodes.AppIdentityUnavailable, ex.Code);
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task PfnFactoryKey_RememberThenClick_FindsControl()
    {
        var env = CreatePfn();
        var factory = new AppIdentityFactory(env.World);
        var appKey = factory.Resolve(1, 1, "Notepad");
        Assert.Contains("Contoso.App_8wekyb3d8bbwe", appKey.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsapps", appKey.Value, StringComparison.OrdinalIgnoreCase);

        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var screenId = remember.RememberScreen(frame, appKey.Value, "home", Spread(), hostWindow: false, appKey.Diagnostics);
        var controlId = remember.RememberControl(frame, appKey.Value, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        env.Frames.Add(live);
        PrimeCapture(env, live);

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.Contains(env.Input.Log, line => line.StartsWith("mouse:Left:down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreIfMinimized_HappensBeforeCapture()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var remembered = ScreenFrame(seed: 3);
        var screenId = remember.RememberScreen(remembered, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(remembered, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);
        PrimeCapture(env, ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false));

        var restoreAtCapture = 0;
        env.Capture.BeforeCapture = () => restoreAtCapture = env.Activator.RestoreCalls;

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.True(restoreAtCapture >= 1);
        Assert.True(env.Capture.CaptureCalls >= 1);
    }

    [Fact]
    public async Task ClickDoesNotEvictCachedVisualizedFrame()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var remembered = ScreenFrame(seed: 3, frameId: "fr1.visual");
        env.Frames.Add(remembered);
        for (var i = 1; i < Limits.V1.MaxCachedFrames; i++)
            env.Frames.Add(ScreenFrame(seed: 3, frameId: "fr1.keep" + i));

        var screenId = remember.RememberScreen(remembered, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(remembered, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);
        PrimeCapture(env, ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false));

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        Assert.True(env.Frames.Require("fr1.visual").ImageReturnedToClient);
        for (var i = 1; i < Limits.V1.MaxCachedFrames; i++)
            Assert.Equal("fr1.keep" + i, env.Frames.Require("fr1.keep" + i).FrameId);
    }

    private Env Create(bool host, string? imagePath = @"c:\apps\app.exe")
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = imagePath };
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
        var activator = new FakeActivator { Foreground = 1 };
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
            activator,
            new FakeHitTester { Hit = 1 },
            input,
            capture,
            new StubHost { Result = host },
            catalog,
            Limits.V1,
            new AppIdentityFactory(world));
        return new Env(click, token, frames, catalog, input, capture, activator, world);
    }

    private Env CreatePfn()
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess
        {
            Pid = 1,
            CreateTimeUtc = 1,
            ImagePath = @"c:\program files\windowsapps\contoso.app\app.exe",
            PackageFamilyName = "Contoso.App_8wekyb3d8bbwe"
        };
        world.Windows[1] = new FakeWindow
        {
            Hwnd = 1,
            Pid = 1,
            ClassName = "Notepad",
            WindowRect = new ScreenRect(0, 0, 400, 400),
            ExtendedFrameBounds = new ScreenRect(0, 0, 400, 400)
        };
        return Build(world, host: false, hwnd: 1, className: "Notepad");
    }

    private Env CreateTwoPathless(string className)
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess { Pid = 1, CreateTimeUtc = 1, ImagePath = null };
        world.Processes[2] = new FakeProcess { Pid = 2, CreateTimeUtc = 2, ImagePath = null };
        world.Windows[1] = new FakeWindow
        {
            Hwnd = 1,
            Pid = 1,
            ClassName = className,
            WindowRect = new ScreenRect(0, 0, 400, 400),
            ExtendedFrameBounds = new ScreenRect(0, 0, 400, 400)
        };
        world.Windows[2] = new FakeWindow
        {
            Hwnd = 2,
            Pid = 2,
            ClassName = className,
            WindowRect = new ScreenRect(0, 0, 400, 400),
            ExtendedFrameBounds = new ScreenRect(0, 0, 400, 400)
        };
        var tokens = new TargetTokenService();
        var token1 = tokens.Issue(1, 1, 1, className);
        var token2 = tokens.Issue(2, 2, 2, className);
        var frames = new FrameCache(Limits.V1);
        var input = new RecordingInjector();
        var capture = new FakeCapture { Width = 400, Height = 400, Pixels = BgraFrames.Solid(400, 400, 0, 0, 0) };
        var activator = new FakeActivator { Foreground = 1 };
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
            activator,
            new FakeHitTester { Hit = 1 },
            input,
            capture,
            new StubHost { Result = false },
            catalog,
            Limits.V1,
            new AppIdentityFactory(world));
        return new Env(click, token1, frames, catalog, input, capture, activator, world, token2);
    }

    private Env Build(FakeWorld world, bool host, nint hwnd, string className)
    {
        var tokens = new TargetTokenService();
        var proc = world.Processes[(uint)hwnd];
        var token = tokens.Issue(hwnd, proc.Pid, proc.CreateTimeUtc, className);
        var frames = new FrameCache(Limits.V1);
        var input = new RecordingInjector();
        var capture = new FakeCapture { Width = 400, Height = 400, Pixels = BgraFrames.Solid(400, 400, 0, 0, 0) };
        var activator = new FakeActivator { Foreground = hwnd };
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
            activator,
            new FakeHitTester { Hit = hwnd },
            input,
            capture,
            new StubHost { Result = host },
            catalog,
            Limits.V1,
            new AppIdentityFactory(world));
        return new Env(click, token, frames, catalog, input, capture, activator, world);
    }

    private static void PrimeCapture(Env env, FrameRecord live)
    {
        env.Capture.Pixels = live.Bgra!;
        env.Capture.Width = live.Width;
        env.Capture.Height = live.Height;
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
        FakeCapture Capture,
        FakeActivator Activator,
        FakeWorld World,
        string? SecondToken = null);
}
