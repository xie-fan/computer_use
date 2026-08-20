using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;
using ComputerUse.Mcp.Input;
using ComputerUse.Mcp.Native;

namespace ComputerUse.Mcp.Tests;

public sealed class InjectionTrackerTests
{
    [Fact]
    public void MatchingUp_PopsLifoWithoutSearch()
    {
        var input = new RecordingInjector();
        var tracker = new InjectionTracker(input);
        tracker.MouseDown(MouseButtonKind.Left);
        tracker.MouseDown(MouseButtonKind.Right);
        tracker.MouseUp(MouseButtonKind.Right);
        Assert.Equal(1, tracker.DownCount);

        tracker.ReleaseAll();
        Assert.Equal(0, tracker.DownCount);
        Assert.Equal(
            [
                "mouse:Left:down",
                "mouse:Right:down",
                "mouse:Right:up",
                "mouse:Left:up"
            ],
            input.Log);
    }

    [Fact]
    public void OutOfOrderUp_RemovesMatchAndKeepsOthers()
    {
        var input = new RecordingInjector();
        var tracker = new InjectionTracker(input);
        tracker.KeyDown(NativeMethods.VK_CONTROL, false);
        tracker.KeyDown(NativeMethods.VK_SHIFT, false);
        tracker.KeyUp(NativeMethods.VK_CONTROL, false);
        Assert.Equal(1, tracker.DownCount);

        tracker.ReleaseAll();
        Assert.Contains("key:16:up", input.Log);
        Assert.Equal(0, tracker.DownCount);
    }

    [Fact]
    public void ReleaseAll_SendsUpsForUnpairedDowns()
    {
        var input = new RecordingInjector();
        var tracker = new InjectionTracker(input);
        tracker.MouseDown(MouseButtonKind.Left);
        tracker.UnicodeDown('a');
        tracker.ReleaseAll();
        Assert.Equal(["unicode:a:up", "mouse:Left:up"], input.Log.TakeLast(2));
    }

    [Fact]
    public void KeyStrokeFailure_ReleasesUnpairedKeys()
    {
        var input = new RecordingInjector { ThrowOnKeyStroke = true };
        var tracker = new InjectionTracker(input);
        Assert.Throws<ComputerUseException>(() => tracker.KeyStroke(0x41, false, true, false, false));
        Assert.Contains("key:65:up", input.Log);
        Assert.Contains("key:17:up", input.Log);
    }
}

public sealed class ClipboardSequenceWaitTests
{
    [Fact]
    public void SequenceChanged_ReturnsImmediately()
    {
        var sleeps = new List<int>();
        var still = ClipboardSequenceWait.StillUnchanged(10, 300, () => 11u, sleeps.Add);
        Assert.False(still);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void ZeroWait_RestoresWithoutSleeping()
    {
        var sleeps = new List<int>();
        var still = ClipboardSequenceWait.StillUnchanged(10, 0, () => 10u, sleeps.Add);
        Assert.True(still);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void SequenceChangesDuringWait_StopsEarly()
    {
        var seq = 10u;
        var sleeps = 0;
        var still = ClipboardSequenceWait.StillUnchanged(10, 300, () => seq, _ =>
        {
            sleeps++;
            seq = 11;
        });
        Assert.False(still);
        Assert.Equal(1, sleeps);
    }
}

public sealed class SendInputAdapterTests
{
    [Fact]
    public void UnicodeText_SendsDownUpPairsInOneCall()
    {
        var calls = new List<int>();
        var adapter = new SendInputAdapter(inputs =>
        {
            calls.Add(inputs.Length);
            return (uint)inputs.Length;
        });

        adapter.UnicodeText("hi");
        Assert.Equal([4], calls);
    }

    [Fact]
    public void KeyStroke_SendsModifiersAndKeyTogether()
    {
        var calls = new List<int>();
        var adapter = new SendInputAdapter(inputs =>
        {
            calls.Add(inputs.Length);
            return (uint)inputs.Length;
        });

        adapter.KeyStroke(0x41, false, true, false, true);
        Assert.Equal([6], calls);
    }

    [Fact]
    public void UnicodeText_ChunksLongRuns()
    {
        var calls = new List<int>();
        var adapter = new SendInputAdapter(inputs =>
        {
            calls.Add(inputs.Length);
            return (uint)inputs.Length;
        });

        adapter.UnicodeText(new string('a', 40));
        Assert.Equal(2, calls.Count);
        Assert.Equal(64, calls[0]);
        Assert.Equal(16, calls[1]);
    }
}

internal sealed class RecordingInjector : IInputInjector
{
    public List<string> Log { get; } = [];
    public bool ThrowOnKeyStroke { get; set; }
    public bool SwapMouseButtons => false;
    public int DoubleClickTimeMs => 500;
    public void RefreshMetrics() { }
    public void MoveAbsoluteVirtualDesk(int physicalX, int physicalY) => Log.Add($"move:{physicalX},{physicalY}");
    public ScreenPoint GetCursorPos() => new(0, 0);
    public void MouseButton(MouseButtonKind logicalButton, bool down) => Log.Add($"mouse:{logicalButton}:{(down ? "down" : "up")}");
    public void Scroll(int dxNotches, int dyNotches) => Log.Add($"scroll:{dxNotches},{dyNotches}");
    public void Key(ushort virtualKey, bool down, bool extended) => Log.Add($"key:{virtualKey}:{(down ? "down" : "up")}");
    public void KeyStroke(ushort virtualKey, bool extended, bool ctrl, bool alt, bool shift)
    {
        if (ThrowOnKeyStroke)
            throw new ComputerUseException(ErrorCodes.ActionFailed, "SendInput was rejected by the OS.");
        Log.Add($"stroke:{virtualKey}:{ctrl}:{alt}:{shift}");
    }
    public void Unicode(char codeUnit, bool down) => Log.Add($"unicode:{codeUnit}:{(down ? "down" : "up")}");
    public void UnicodeText(ReadOnlySpan<char> codeUnits) => Log.Add($"unicode-text:{codeUnits.ToString()}");
}
