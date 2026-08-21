using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Memory;
using ComputerUse.Mcp.Services;
using ComputerUse.Mcp.Tests.Support;

namespace ComputerUse.Mcp.Tests;

public sealed class RememberServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cu-remember-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryCatalog _catalog;
    private readonly RememberService _remember;

    public RememberServiceTests()
    {
        _catalog = new MemoryCatalog(_root, Limits.V1);
        _remember = new RememberService(_catalog, Limits.V1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void UnvisualizedFrame_IsStaleOrFrameNotVisualized()
    {
        var bgra = UniqueFrame(400, 400);
        var frame = TestFrames.Create(400, 400, bgra, visualized: false);
        var boxes = SpreadBoxes(400);
        var ex = Assert.Throws<ComputerUseException>(() =>
            _remember.RememberScreen(frame, "app.a", "home", boxes, hostWindow: false));
        Assert.True(ex.Code is ErrorCodes.FrameNotVisualized or ErrorCodes.StaleCapture);
        Assert.Empty(_catalog.List("app.a"));
    }

    [Fact]
    public void HostWindow_IsHostWindowForbidden()
    {
        var bgra = UniqueFrame(400, 400);
        var frame = TestFrames.Create(400, 400, bgra);
        var ex = Assert.Throws<ComputerUseException>(() =>
            _remember.RememberScreen(frame, "app.a", "home", SpreadBoxes(400), hostWindow: true));
        Assert.Equal(ErrorCodes.HostWindowForbidden, ex.Code);
        Assert.Empty(_catalog.List("app.a"));
    }

    [Fact]
    public void SingleFingerprintOnLargeWindow_Rejected()
    {
        var bgra = UniqueFrame(400, 400);
        var frame = TestFrames.Create(400, 400, bgra);
        var one = new[] { new PixelBox(8, 8, 32, 32) };
        Assert.Throws<ComputerUseException>(() =>
            _remember.RememberScreen(frame, "app.a", "home", one, hostWindow: false));
        Assert.Empty(_catalog.List("app.a"));
    }

    [Fact]
    public void TinyDialog_AllowsOneFingerprint()
    {
        var bgra = UniqueFrame(180, 180);
        var frame = TestFrames.Create(180, 180, bgra);
        var id = _remember.RememberScreen(frame, "app.tiny", "dlg", [new PixelBox(8, 8, 32, 32)], hostWindow: false);
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Contains(_catalog.List("app.tiny"), s => s.ScreenId == id);
    }

    [Fact]
    public void SameFingerprints_AreIdempotentScreenId()
    {
        var bgra = UniqueFrame(400, 400);
        var frame = TestFrames.Create(400, 400, bgra);
        var boxes = SpreadBoxes(400);
        var first = _remember.RememberScreen(frame, "app.a", "home", boxes, hostWindow: false);
        var second = _remember.RememberScreen(frame, "app.a", "home", boxes, hostWindow: false);
        Assert.Equal(first, second);
        Assert.Single(_catalog.List("app.a"));
    }

    [Fact]
    public void LowEntropyBox_IsLowEntropyCrop()
    {
        var bgra = BgraFrames.Solid(400, 400, 40, 40, 40);
        var frame = TestFrames.Create(400, 400, bgra);
        var ex = Assert.Throws<ComputerUseException>(() =>
            _remember.RememberScreen(frame, "app.a", "home", SpreadBoxes(400), hostWindow: false));
        Assert.Equal(ErrorCodes.LowEntropyCrop, ex.Code);
        Assert.Empty(_catalog.List("app.a"));
    }

    private static byte[] UniqueFrame(int w, int h)
    {
        var frame = BgraFrames.Solid(w, h, 20, 20, 20);
        BgraFrames.Paste(frame, w, BgraFrames.Checker(32, 32, 2), 32, 32, 8, 8);
        if (w >= 360 && h >= 360)
            BgraFrames.Paste(frame, w, BgraFrames.Noise(32, 32, 7), 32, 32, w - 40, h - 40);
        else
            BgraFrames.Paste(frame, w, BgraFrames.Noise(32, 32, 7), 32, 32, 8, 8);
        return frame;
    }

    private static PixelBox[] SpreadBoxes(int size) =>
    [
        new(8, 8, 32, 32),
        new(size - 40, size - 40, 32, 32)
    ];
}
