using System.Text.Json;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Tests;

public sealed class ActionPrevalidatorTests
{
    private static readonly Limits Limits = Limits.V1;

    [Fact]
    public void ExtraProperty_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"wait","ms":10,"nope":true}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void ClickOutOfSchema_NonInteger_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"click","x":1.5,"y":2}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void TooManyActions_HasDedicatedCode()
    {
        var actions = Enumerable.Range(0, 33).Select(_ => """{"type":"wait","ms":1}""");
        var json = "[" + string.Join(",", actions) + "]";
        var ex = Assert.Throws<ComputerUseException>(() => Parse(json));
        Assert.Equal(ErrorCodes.TooManyActions, ex.Code);
    }

    [Fact]
    public void TextOverLimit_IsPayloadTooLarge()
    {
        var value = new string('a', Limits.MaxTextUtf16 + 1);
        var json = $$"""[{"type":"text","value":"{{value}}"}]""";
        var ex = Assert.Throws<ComputerUseException>(() => Parse(json));
        Assert.Equal(ErrorCodes.PayloadTooLarge, ex.Code);
    }

    [Fact]
    public void UnpairedSurrogate_IsDetected()
    {
        Assert.True(ActionPrevalidator.HasUnpairedSurrogate("\uD800"));
        Assert.False(ActionPrevalidator.HasUnpairedSurrogate("ok"));
        Assert.False(ActionPrevalidator.HasUnpairedSurrogate("\uD83D\uDE00"));
    }

    [Fact]
    public void DownWithoutMatchingUp_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"down","button":"left"}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void UpWithoutDown_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"up","button":"left"}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void AltF4_NotLast_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"key","key":"F4","modifiers":["Alt"]},{"type":"wait","ms":1}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void AltTab_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"key","key":"Tab","modifiers":["Alt"]}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void WinModifier_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() => Parse("""[{"type":"key","key":"E","modifiers":["Win"]}]"""));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void MissingFrameId_IsInvalidAction()
    {
        var ex = Assert.Throws<ComputerUseException>(() =>
            ActionPrevalidator.Parse("tok", "", JsonDocument.Parse("""[{"type":"wait","ms":1}]""").RootElement, 100, null, Limits));
        Assert.Equal(ErrorCodes.InvalidAction, ex.Code);
    }

    [Fact]
    public void ValidClickAndWait_Parses()
    {
        var parsed = Parse("""[{"type":"click","x":1,"y":2,"button":"left","count":2},{"type":"wait","ms":10}]""");
        Assert.True(parsed.HasPointerActions);
        Assert.Equal(2, parsed.Actions.Count);
        Assert.Equal(100, parsed.PauseMs);
    }

    [Fact]
    public void KeyOnly_HasNoPointerActions()
    {
        var parsed = Parse("""[{"type":"key","key":"Enter"}]""");
        Assert.False(parsed.HasPointerActions);
    }

    private static ParsedOperateRequest Parse(string actionsJson) =>
        ActionPrevalidator.Parse("tok", "frame", JsonDocument.Parse(actionsJson).RootElement, null, null, Limits);
}
