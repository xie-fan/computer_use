using System.Globalization;
using System.Text.Json;

namespace ComputerUse.Mcp.Domain;

internal abstract record WindowAction(string Type);

internal sealed record ClickAction(int X, int Y, MouseButtonKind Button, int Count, string? FrameId) : WindowAction("click");
internal sealed record MoveAction(int X, int Y, string? FrameId) : WindowAction("move");
internal sealed record ButtonDownAction(MouseButtonKind Button, int? X, int? Y, string? FrameId) : WindowAction("down");
internal sealed record ButtonUpAction(MouseButtonKind Button, int? X, int? Y, string? FrameId) : WindowAction("up");
internal sealed record ScrollAction(int X, int Y, int Dy, int Dx, string? FrameId) : WindowAction("scroll");
internal sealed record KeyAction(string Key, IReadOnlyList<string> Modifiers, bool IsAltF4Terminator) : WindowAction("key");
internal sealed record TextAction(string Value) : WindowAction("text");
internal sealed record PasteAction(string Value) : WindowAction("paste");
internal sealed record WaitAction(int Ms) : WindowAction("wait");

internal sealed class ParsedOperateRequest
{
    public required string TargetToken { get; init; }
    public required string FrameId { get; init; }
    public required IReadOnlyList<WindowAction> Actions { get; init; }
    public required int PauseMs { get; init; }
    public string? OperationId { get; init; }
    public required bool HasPointerActions { get; init; }
}

internal static class KeyWhitelist
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "Enter", "Tab", "Escape", "Backspace", "Delete", "Space",
        "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
    };

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "Ctrl", "Alt", "Shift"
    };

    public static bool IsAllowedKey(string key)
    {
        if (Keys.Contains(key))
            return true;
        if (key.Length == 1)
        {
            var c = key[0];
            if (c is >= 'A' and <= 'Z')
                return true;
            if (c is >= '0' and <= '9')
                return true;
        }
        return false;
    }

    public static bool IsAllowedModifier(string modifier) => Modifiers.Contains(modifier);

    public static bool IsForbiddenCombo(string key, IReadOnlyList<string> modifiers)
    {
        var set = new HashSet<string>(modifiers, StringComparer.Ordinal);
        if (set.Contains("Win"))
            return true;
        if (set.Contains("Alt") && key == "Tab")
            return true;
        if (set.Contains("Ctrl") && set.Contains("Shift") && key == "Escape")
            return true;
        if (set.Contains("Ctrl") && set.Contains("Alt") && key == "Delete")
            return true;
        return false;
    }

    public static bool IsAltF4(string key, IReadOnlyList<string> modifiers) =>
        key == "F4" && modifiers.Contains("Alt", StringComparer.Ordinal) && !modifiers.Contains("Ctrl", StringComparer.Ordinal);

    public static ushort VirtualKey(string key) => key switch
    {
        "Enter" => 0x0D,
        "Tab" => 0x09,
        "Escape" => 0x1B,
        "Backspace" => 0x08,
        "Delete" => 0x2E,
        "Space" => 0x20,
        "Home" => 0x24,
        "End" => 0x23,
        "PageUp" => 0x21,
        "PageDown" => 0x22,
        "Left" => 0x25,
        "Right" => 0x27,
        "Up" => 0x26,
        "Down" => 0x28,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        _ when key.Length == 1 && key[0] is >= 'A' and <= 'Z' => key[0],
        _ when key.Length == 1 && key[0] is >= '0' and <= '9' => key[0],
        _ => throw new ComputerUseException(ErrorCodes.InvalidAction, $"Key '{key}' is not in the whitelist.")
    };

    public static bool IsExtendedKey(string key) => key is "Delete" or "Home" or "End" or "PageUp" or "PageDown" or "Left" or "Right" or "Up" or "Down";
}

internal static class ActionPrevalidator
{
    private static readonly HashSet<string> ClickProps = ["type", "x", "y", "button", "count", "frameId"];
    private static readonly HashSet<string> MoveProps = ["type", "x", "y", "frameId"];
    private static readonly HashSet<string> DownUpProps = ["type", "button", "x", "y", "frameId"];
    private static readonly HashSet<string> ScrollProps = ["type", "x", "y", "dy", "dx", "frameId"];
    private static readonly HashSet<string> KeyProps = ["type", "key", "modifiers"];
    private static readonly HashSet<string> TextProps = ["type", "value"];
    private static readonly HashSet<string> PasteProps = ["type", "value"];
    private static readonly HashSet<string> WaitProps = ["type", "ms"];

    public static ParsedOperateRequest Parse(
        string targetToken,
        string frameId,
        JsonElement actionsElement,
        int? pauseMs,
        string? operationId,
        Limits limits)
    {
        if (string.IsNullOrWhiteSpace(targetToken))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "targetToken is required.");
        if (string.IsNullOrWhiteSpace(frameId))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "frameId is required.");
        if (operationId is { Length: > 0 } && operationId.Length > limits.MaxOperationIdChars)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "operationId is too long.");

        var pause = pauseMs ?? limits.DefaultPauseMs;
        if (pause < 0 || pause > limits.MaxPauseMs)
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"pauseMs must be between 0 and {limits.MaxPauseMs}.");

        if (actionsElement.ValueKind != JsonValueKind.Array)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "actions must be a JSON array.");

        var count = actionsElement.GetArrayLength();
        if (count == 0)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "actions must contain at least one item.");
        if (count > limits.MaxActionsPerRequest)
            throw new ComputerUseException(ErrorCodes.TooManyActions, $"At most {limits.MaxActionsPerRequest} actions are allowed.");

        var actions = new List<WindowAction>(count);
        var hasPointer = false;
        for (var i = 0; i < count; i++)
        {
            var item = actionsElement[i];
            WindowAction parsed;
            try
            {
                parsed = ParseOne(item, frameId, limits);
            }
            catch (ComputerUseException ex)
            {
                throw new ComputerUseException(ex.Code, $"Action {i}: {ex.Message}", new { index = i });
            }

            if (IsPointer(parsed))
                hasPointer = true;
            actions.Add(parsed);
        }

        ValidateButtonStateMachine(actions);
        ValidateAltF4Terminator(actions);

        return new ParsedOperateRequest
        {
            TargetToken = targetToken,
            FrameId = frameId,
            Actions = actions,
            PauseMs = pause,
            OperationId = string.IsNullOrWhiteSpace(operationId) ? null : operationId,
            HasPointerActions = hasPointer
        };
    }

    public static bool IsPointer(WindowAction action) =>
        action is ClickAction or MoveAction or ButtonDownAction or ButtonUpAction or ScrollAction;

    private static void ValidateAltF4Terminator(IReadOnlyList<WindowAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i] is KeyAction { IsAltF4Terminator: true } && i != actions.Count - 1)
            {
                throw new ComputerUseException(
                    ErrorCodes.InvalidAction,
                    "Alt+F4 must be the last action in the request.");
            }
        }
    }

    private static void ValidateButtonStateMachine(IReadOnlyList<WindowAction> actions)
    {
        var down = new Dictionary<MouseButtonKind, int>();
        foreach (var button in new[] { MouseButtonKind.Left, MouseButtonKind.Right, MouseButtonKind.Middle })
            down[button] = 0;

        foreach (var action in actions)
        {
            switch (action)
            {
                case ButtonDownAction d:
                    down[d.Button]++;
                    break;
                case ButtonUpAction u:
                    down[u.Button]--;
                    if (down[u.Button] < 0)
                    {
                        throw new ComputerUseException(
                            ErrorCodes.InvalidAction,
                            "Mouse up without a matching down in this request.");
                    }
                    break;
                case ClickAction:
                    break;
            }
        }

        if (down.Values.Any(v => v != 0))
        {
            throw new ComputerUseException(
                ErrorCodes.InvalidAction,
                "Each mouse down in this request must have a matching up.");
        }
    }

    private static WindowAction ParseOne(JsonElement item, string requestFrameId, Limits limits)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Each action must be an object.");

        if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Action type is required.");

        var type = typeEl.GetString()!;
        return type switch
        {
            "click" => ParseClick(item, requestFrameId),
            "move" => ParseMove(item, requestFrameId),
            "down" => ParseDownUp(item, requestFrameId, down: true),
            "up" => ParseDownUp(item, requestFrameId, down: false),
            "scroll" => ParseScroll(item, requestFrameId),
            "key" => ParseKey(item),
            "text" => ParseText(item, limits, paste: false),
            "paste" => ParseText(item, limits, paste: true),
            "wait" => ParseWait(item, limits),
            _ => throw new ComputerUseException(ErrorCodes.InvalidAction, $"Unknown action type '{type}'.")
        };
    }

    private static ClickAction ParseClick(JsonElement item, string requestFrameId)
    {
        RejectUnknown(item, ClickProps);
        var x = RequireInt(item, "x");
        var y = RequireInt(item, "y");
        var button = OptionalButton(item);
        var count = 1;
        if (item.TryGetProperty("count", out var countEl))
        {
            count = RequireIntValue(countEl, "count");
            if (count is not (1 or 2))
                throw new ComputerUseException(ErrorCodes.InvalidAction, "click.count must be 1 or 2.");
        }

        return new ClickAction(x, y, button, count, OptionalInheritedFrameId(item, requestFrameId));
    }

    private static MoveAction ParseMove(JsonElement item, string requestFrameId)
    {
        RejectUnknown(item, MoveProps);
        return new MoveAction(RequireInt(item, "x"), RequireInt(item, "y"), OptionalInheritedFrameId(item, requestFrameId));
    }

    private static WindowAction ParseDownUp(JsonElement item, string requestFrameId, bool down)
    {
        RejectUnknown(item, DownUpProps);
        var button = OptionalButton(item);
        int? x = item.TryGetProperty("x", out var xEl) ? RequireIntValue(xEl, "x") : null;
        int? y = item.TryGetProperty("y", out var yEl) ? RequireIntValue(yEl, "y") : null;
        if (x is null ^ y is null)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "x and y must both be present or both omitted.");
        var frameId = OptionalInheritedFrameId(item, requestFrameId);
        return down
            ? new ButtonDownAction(button, x, y, frameId)
            : new ButtonUpAction(button, x, y, frameId);
    }

    private static ScrollAction ParseScroll(JsonElement item, string requestFrameId)
    {
        RejectUnknown(item, ScrollProps);
        var dx = item.TryGetProperty("dx", out var dxEl) ? RequireIntValue(dxEl, "dx") : 0;
        return new ScrollAction(
            RequireInt(item, "x"),
            RequireInt(item, "y"),
            RequireInt(item, "dy"),
            dx,
            OptionalInheritedFrameId(item, requestFrameId));
    }

    private static KeyAction ParseKey(JsonElement item)
    {
        RejectUnknown(item, KeyProps);
        if (!item.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "key is required.");
        var key = keyEl.GetString()!;
        if (!KeyWhitelist.IsAllowedKey(key))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Key is not in the whitelist. Letters must be A–Z.");

        var modifiers = new List<string>();
        if (item.TryGetProperty("modifiers", out var modsEl))
        {
            if (modsEl.ValueKind != JsonValueKind.Array)
                throw new ComputerUseException(ErrorCodes.InvalidAction, "modifiers must be an array.");
            foreach (var m in modsEl.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.String)
                    throw new ComputerUseException(ErrorCodes.InvalidAction, "Each modifier must be a string.");
                var name = m.GetString()!;
                if (name == "Win" || !KeyWhitelist.IsAllowedModifier(name))
                    throw new ComputerUseException(ErrorCodes.InvalidAction, "Modifier is not allowed. Win is never permitted.");
                if (!modifiers.Contains(name, StringComparer.Ordinal))
                    modifiers.Add(name);
            }
        }

        if (KeyWhitelist.IsForbiddenCombo(key, modifiers))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "This key combination is denied.");

        return new KeyAction(key, modifiers, KeyWhitelist.IsAltF4(key, modifiers));
    }

    private static WindowAction ParseText(JsonElement item, Limits limits, bool paste)
    {
        RejectUnknown(item, paste ? PasteProps : TextProps);
        if (!item.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "value is required.");
        var value = valueEl.GetString()!;
        if (value.Length > limits.MaxTextUtf16)
            throw new ComputerUseException(ErrorCodes.PayloadTooLarge, $"Text exceeds {limits.MaxTextUtf16} UTF-16 code units.");
        if (HasUnpairedSurrogate(value))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Text contains an unpaired UTF-16 surrogate.");
        return paste ? new PasteAction(value) : new TextAction(value);
    }

    private static WaitAction ParseWait(JsonElement item, Limits limits)
    {
        RejectUnknown(item, WaitProps);
        var ms = RequireInt(item, "ms");
        if (ms < 1 || ms > limits.MaxWaitMs)
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"wait.ms must be between 1 and {limits.MaxWaitMs}.");
        return new WaitAction(ms);
    }

    private static string OptionalInheritedFrameId(JsonElement item, string requestFrameId)
    {
        if (!item.TryGetProperty("frameId", out var el))
            return requestFrameId;
        if (el.ValueKind != JsonValueKind.String)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "frameId must be a string.");
        var value = el.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "frameId must be non-empty.");
        if (!string.Equals(value, requestFrameId, StringComparison.Ordinal))
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Action frameId must match the request frameId.");
        return value;
    }

    private static MouseButtonKind OptionalButton(JsonElement item)
    {
        if (!item.TryGetProperty("button", out var el))
            return MouseButtonKind.Left;
        if (el.ValueKind != JsonValueKind.String)
            throw new ComputerUseException(ErrorCodes.InvalidAction, "button must be a string.");
        return el.GetString() switch
        {
            "left" => MouseButtonKind.Left,
            "right" => MouseButtonKind.Right,
            "middle" => MouseButtonKind.Middle,
            _ => throw new ComputerUseException(ErrorCodes.InvalidAction, "button must be left, right, or middle.")
        };
    }

    private static int RequireInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} is required.");
        return RequireIntValue(el, name);
    }

    private static int RequireIntValue(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value))
            throw new ComputerUseException(ErrorCodes.InvalidAction, $"{name} must be a finite integer.");
        return value;
    }

    private static void RejectUnknown(JsonElement obj, HashSet<string> allowed)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
                throw new ComputerUseException(ErrorCodes.InvalidAction, $"Unknown property '{prop.Name}'.");
        }
    }

    internal static bool HasUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return true;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return true;
            }
        }
        return false;
    }
}
