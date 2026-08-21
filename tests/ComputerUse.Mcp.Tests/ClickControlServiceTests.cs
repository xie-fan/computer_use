using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Mcp;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;
using ComputerUse.Mcp.Tests.Support;
using System.Diagnostics;
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
    public async Task SuccessfulClick_WritesLastMatchedAtVisibleToList()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = ScreenFrame(seed: 3);
        env.Frames.Add(frame);
        var screenId = remember.RememberScreen(frame, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(frame, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        Assert.Null(Assert.Single(env.Catalog.List(AppKeyValue)).LastMatchedAt);
        Assert.Null(Assert.Single(Assert.Single(env.Catalog.List(AppKeyValue)).Controls).LastMatchedAt);

        var live = ScreenFrame(seed: 3, frameId: "fr1.live", visualized: false);
        env.Frames.Add(live);
        env.Capture.Pixels = live.Bgra!;
        env.Capture.Width = live.Width;
        env.Capture.Height = live.Height;

        await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);

        var listed = env.Catalog.List(AppKeyValue);
        var screen = Assert.Single(listed);
        Assert.Equal(screenId, screen.ScreenId);
        Assert.NotNull(screen.LastMatchedAt);
        var control = Assert.Single(screen.Controls);
        Assert.Equal(controlId, control.ControlId);
        Assert.NotNull(control.LastMatchedAt);

        var payload = new ListRememberedResult
        {
            Screens = listed,
            Controls = listed.SelectMany(s => s.Controls).ToList()
        };
        using var doc = JsonDocument.Parse(ToolResults.SerializeStructured(payload).GetRawText());
        Assert.True(doc.RootElement.GetProperty("screens")[0].TryGetProperty("lastMatchedAt", out var screenMatched));
        Assert.False(string.IsNullOrWhiteSpace(screenMatched.GetString()));
        Assert.True(doc.RootElement.GetProperty("controls")[0].TryGetProperty("lastMatchedAt", out var controlMatched));
        Assert.False(string.IsNullOrWhiteSpace(controlMatched.GetString()));
    }

    [Fact]
    public async Task FailedClick_DoesNotWriteLastMatchedAt()
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

        var listed = env.Catalog.List(AppKeyValue);
        Assert.Null(Assert.Single(listed).LastMatchedAt);
        Assert.Null(Assert.Single(Assert.Single(listed).Controls).LastMatchedAt);
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

    [Fact]
    public async Task LargeTemplateMissingOnHdFrame_SkipsFullFrameAndReturnsNotFoundQuickly()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var remembered = HdFrame(seed: 3, withBigControl: true);
        var screenId = remember.RememberScreen(
            remembered, AppKeyValue, "home", HdSpread(), hostWindow: false);
        remember.RememberControl(
            remembered, AppKeyValue, screenId, "anchor", new PixelBox(8, 8, 32, 32), hostWindow: false);
        var controlId = remember.RememberControl(
            remembered, AppKeyValue, screenId, "big", new PixelBox(200, 200, 64, 64), hostWindow: false);

        PrimeCapture(env, HdFrame(seed: 3, withBigControl: false, frameId: "fr1.live", visualized: false));

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        sw.Stop();

        Assert.Equal(ErrorCodes.TemplateNotFound, ex.Code);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task Template256MissingOnHdFrame_SkipsFullFrameAndReturnsNotFoundQuickly()
    {
        var env = Create(host: false);
        var remember = new RememberService(env.Catalog, Limits.V1);
        var remembered = HdFrame(seed: 3, withBigControl: true, bigControlEdge: 256);
        var screenId = remember.RememberScreen(
            remembered, AppKeyValue, "home", HdSpread(), hostWindow: false);
        remember.RememberControl(
            remembered, AppKeyValue, screenId, "anchor", new PixelBox(8, 8, 32, 32), hostWindow: false);
        var controlId = remember.RememberControl(
            remembered, AppKeyValue, screenId, "huge", new PixelBox(200, 200, 256, 256), hostWindow: false);

        PrimeCapture(env, HdFrame(seed: 3, withBigControl: false, frameId: "fr1.live", visualized: false));

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None));
        sw.Stop();

        Assert.Equal(ErrorCodes.TemplateNotFound, ex.Code);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task MidSizeTemplateMissingOnHdFrame_TimesOutAndUnblocksCoordinator()
    {
        var limits = Limits.V1;
        var coordinator = new DesktopOperationCoordinator(limits);
        var env = Create(host: false, coordinator: coordinator, limits: limits);
        var remember = new RememberService(env.Catalog, limits);
        var remembered = HdFrame(seed: 3, withBigControl: false, withMidControl: true);
        var screenId = remember.RememberScreen(
            remembered, AppKeyValue, "home", HdSpread(), hostWindow: false);
        remember.RememberControl(
            remembered, AppKeyValue, screenId, "anchor", new PixelBox(8, 8, 32, 32), hostWindow: false);
        var controlId = remember.RememberControl(
            remembered, AppKeyValue, screenId, "mid", new PixelBox(400, 200, 32, 32), hostWindow: false);

        PrimeCapture(env, HdFrame(seed: 3, withBigControl: false, frameId: "fr1.live", visualized: false));

        var sw = Stopwatch.StartNew();
        var clickTask = env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        var other = coordinator.RunAsync(_ => Task.FromResult(7), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ComputerUseException>(() => clickTask);
        Assert.Equal(7, await other);
        sw.Stop();

        Assert.Equal(ErrorCodes.TemplateNotFound, ex.Code);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(limits.RequestDeadlineMs), sw.Elapsed.ToString());
        Assert.Empty(env.Input.Log);
    }

    [Fact]
    public async Task ScaledLiveFrame_MapsClickThroughCoordinateMapper()
    {
        var limits = Limits.V1 with { MaxReturnedLongEdge = 400 };
        var env = Create(host: false, limits: limits);
        env.World.Windows[1].WindowRect = new ScreenRect(0, 0, 800, 800);
        env.World.Windows[1].ExtendedFrameBounds = new ScreenRect(0, 0, 800, 800);

        var remember = new RememberService(env.Catalog, limits);
        var remembered = ScreenFrame(seed: 3);
        env.Frames.Add(remembered);
        var screenId = remember.RememberScreen(remembered, AppKeyValue, "home", Spread(), hostWindow: false);
        var controlId = remember.RememberControl(
            remembered, AppKeyValue, screenId, "go", new PixelBox(8, 8, 32, 32), hostWindow: false);

        var livePixels = BgraFrames.ScaleNearest(remembered.Bgra!, 400, 400, 2);
        env.Capture.Pixels = livePixels;
        env.Capture.Width = 800;
        env.Capture.Height = 800;

        var clicked = await env.Click.ClickAsync(env.Token, controlId, null, CancellationToken.None);
        var move = Assert.Single(env.Input.Log, line => line.StartsWith("move:", StringComparison.Ordinal));
        var parts = move["move:".Length..].Split(',');
        var x = int.Parse(parts[0]);
        var y = int.Parse(parts[1]);
        Assert.InRange(x, 40, 56);
        Assert.InRange(y, 40, 56);
        Assert.Equal(controlId, clicked.ControlId);
    }

    private Env Create(
        bool host,
        string? imagePath = @"c:\apps\app.exe",
        Limits? limits = null,
        DesktopOperationCoordinator? coordinator = null)
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
        return Build(world, host, 1, "Notepad", limits, coordinator);
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

    private Env Build(
        FakeWorld world,
        bool host,
        nint hwnd,
        string className,
        Limits? limits = null,
        DesktopOperationCoordinator? coordinator = null)
    {
        limits ??= Limits.V1;
        coordinator ??= new DesktopOperationCoordinator(limits);
        var tokens = new TargetTokenService();
        var proc = world.Processes[(uint)hwnd];
        var token = tokens.Issue(hwnd, proc.Pid, proc.CreateTimeUtc, className);
        var frames = new FrameCache(limits);
        var input = new RecordingInjector();
        var capture = new FakeCapture { Width = 400, Height = 400, Pixels = BgraFrames.Solid(400, 400, 0, 0, 0) };
        var activator = new FakeActivator { Foreground = hwnd };
        var catalog = new MemoryCatalog(_root, limits);
        var click = new ClickControlService(
            coordinator,
            new OperationIdCache(limits),
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
            limits,
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

    private static PixelBox[] HdSpread() => [new(8, 8, 32, 32), new(1200, 680, 32, 32)];

    private static FrameRecord HdFrame(
        int seed,
        bool withBigControl,
        string frameId = "fr1.hd",
        bool visualized = true,
        int bigControlEdge = 64,
        bool withMidControl = false)
    {
        const int width = 1280;
        const int height = 720;
        var bgra = BgraFrames.Solid(width, height, 20, 20, 20);
        BgraFrames.Paste(bgra, width, BgraFrames.Checker(32, 32, 2), 32, 32, 8, 8);
        BgraFrames.Paste(bgra, width, BgraFrames.Noise(32, 32, seed), 32, 32, 1200, 680);
        if (withBigControl)
            BgraFrames.Paste(bgra, width, BgraFrames.Checker(bigControlEdge, bigControlEdge, 3), bigControlEdge, bigControlEdge, 200, 200);
        if (withMidControl)
            BgraFrames.Paste(bgra, width, BgraFrames.Checker(32, 32, 7), 32, 32, 400, 200);
        return TestFrames.Create(width, height, bgra, visualized, frameId);
    }

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
