using ComputerUse.Mcp.Coordination;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Identity;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Fakes;
using ComputerUse.Mcp.Tests.Support;
using System.Text.Json;

namespace ComputerUse.Mcp.Tests;

public sealed class ObserveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cu-obs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Success_HasNoPngPayload()
    {
        var env = Create(host: false);
        var result = await env.Svc.ObserveAsync(env.Token, CancellationToken.None);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("data:image", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Png\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Visualized);
        Assert.False(string.IsNullOrWhiteSpace(result.FrameId));
    }

    [Fact]
    public async Task HostWindow_EmptyControls()
    {
        var env = Create(host: true);
        var result = await env.Svc.ObserveAsync(env.Token, CancellationToken.None);
        Assert.True(result.HostWindow);
        Assert.Null(result.ScreenId);
        Assert.Empty(result.Controls);
    }

    [Fact]
    public async Task HostWindow_WithNoImagePath_StillEmptyNotIdentityError()
    {
        var env = Create(host: true, imagePath: null);
        var result = await env.Svc.ObserveAsync(env.Token, CancellationToken.None);
        Assert.True(result.HostWindow);
        Assert.Empty(result.Controls);
    }

    [Fact]
    public async Task CachedFrame_IsNotVisualized()
    {
        var env = Create(host: false);
        var result = await env.Svc.ObserveAsync(env.Token, CancellationToken.None);
        var frame = env.Frames.Require(result.FrameId);
        Assert.False(frame.ImageReturnedToClient);
        Assert.NotNull(frame.Bgra);
    }

    [Fact]
    public async Task PathlessNonHost_IsAppIdentityUnavailable()
    {
        var env = Create(host: false, imagePath: null);
        var ex = await Assert.ThrowsAsync<ComputerUseException>(() =>
            env.Svc.ObserveAsync(env.Token, CancellationToken.None));
        Assert.Equal(ErrorCodes.AppIdentityUnavailable, ex.Code);
    }

    [Fact]
    public async Task PfnFactoryKey_RememberThenObserve_FindsScreen()
    {
        var env = Create(
            host: false,
            imagePath: @"c:\program files\windowsapps\contoso.app\app.exe",
            packageFamilyName: "Contoso.App_8wekyb3d8bbwe",
            width: 400);
        var factory = new AppIdentityFactory(env.World);
        var appKey = factory.Resolve(1, 1, "Notepad");
        Assert.Contains("Contoso.App_8wekyb3d8bbwe", appKey.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("windowsapps", appKey.Value, StringComparison.OrdinalIgnoreCase);

        var bgra = UniqueObserveFrame();
        env.Capture.Pixels = bgra;
        env.Capture.Width = 400;
        env.Capture.Height = 400;
        var remember = new RememberService(env.Catalog, Limits.V1);
        var frame = TestFrames.Create(400, 400, bgra, visualized: true);
        var screenId = remember.RememberScreen(
            frame,
            appKey.Value,
            "home",
            [new PixelBox(8, 8, 32, 32), new PixelBox(320, 320, 32, 32)],
            hostWindow: false,
            appKey.Diagnostics);

        var result = await env.Svc.ObserveAsync(env.Token, CancellationToken.None);
        Assert.Equal(screenId, result.ScreenId);
        Assert.False(result.HostWindow);
    }

    private ObserveEnv Create(
        bool host,
        string? imagePath = @"c:\apps\app.exe",
        string? packageFamilyName = null,
        int width = 200)
    {
        var world = new FakeWorld();
        world.Processes[1] = new FakeProcess
        {
            Pid = 1,
            CreateTimeUtc = 1,
            ImagePath = imagePath,
            PackageFamilyName = packageFamilyName
        };
        world.Windows[1] = new FakeWindow
        {
            Hwnd = 1,
            Pid = 1,
            ClassName = "Notepad",
            WindowRect = new ScreenRect(0, 0, width, width),
            ExtendedFrameBounds = new ScreenRect(0, 0, width, width)
        };
        var tokens = new TargetTokenService();
        var token = tokens.Issue(1, 1, 1, "Notepad");
        var frames = new FrameCache(Limits.V1);
        var capture = new FakeCapture { Width = 80, Height = 80, Pixels = BgraFrames.Checker(80, 80) };
        var hostResolver = new StubHost { Result = host };
        var catalog = new MemoryCatalog(_root, Limits.V1);
        var svc = new ObserveService(
            new DesktopOperationCoordinator(Limits.V1),
            tokens,
            frames,
            world,
            new FakeMonitors(),
            world,
            new FakeDesktops(),
            new FakeSession(),
            new FakeActivator { Foreground = 1 },
            capture,
            hostResolver,
            catalog,
            Limits.V1,
            new AppIdentityFactory(world));
        return new ObserveEnv(svc, frames, token, catalog, world, capture);
    }

    private static byte[] UniqueObserveFrame()
    {
        var frame = BgraFrames.Solid(400, 400, 20, 20, 20);
        BgraFrames.Paste(frame, 400, BgraFrames.Checker(32, 32, 2), 32, 32, 8, 8);
        BgraFrames.Paste(frame, 400, BgraFrames.Noise(32, 32, 7), 32, 32, 320, 320);
        return frame;
    }

    private sealed record ObserveEnv(
        ObserveService Svc,
        FrameCache Frames,
        string Token,
        MemoryCatalog Catalog,
        FakeWorld World,
        FakeCapture Capture);
}
