using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class Program
{
    private static readonly string[] RequiredTools =
    [
        "list_windows", "screenshot_window", "operate_window",
        "observe_window", "remember_screen", "remember_control",
        "click_control", "list_remembered", "forget_controls"
    ];

    private static readonly List<string> Failures = [];
    private static int _screenshotCalls;

    public static async Task<int> Main(string[] args)
    {
        var exe = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "win-x64", "ComputerUse.Mcp.exe"));
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine("ComputerUse.Mcp.exe not found: " + exe);
            return 2;
        }

        Process? notepad = null;
        string? screenId = null;
        string? controlId = null;
        string? notepadToken = null;
        try
        {
            notepad = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
            await Task.Delay(1200);

            await using (var cold = await ConnectAsync(exe))
            {
                await CheckToolsAsync(cold);
                var list = await CallJsonAsync(cold, "list_windows", []);
                AssertV1ListShape(list);

                notepadToken = FindNotepadToken(list);
                var hostToken = FindHostToken(list);
                Check("found Notepad window", notepadToken is not null);
                if (notepadToken is null)
                    return Finish();

                var shot = await ScreenshotAsync(cold, notepadToken);
                var frameId = shot.GetProperty("frameId").GetString()!;
                var width = shot.GetProperty("width").GetInt32();
                var height = shot.GetProperty("height").GetInt32();

                await CallJsonAsync(cold, "operate_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["frameId"] = frameId,
                    ["actions"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "paste", ["value"] = "cu-v2-accept " + Guid.NewGuid().ToString("N")[..8] }
                    }
                });

                var (shot2, png) = await ScreenshotWithPngAsync(cold, notepadToken);
                frameId = shot2.GetProperty("frameId").GetString()!;
                width = shot2.GetProperty("width").GetInt32();
                height = shot2.GetProperty("height").GetInt32();
                var boxes = PickSpreadBoxes(png, width, height);
                Check("picked two high-entropy fingerprint boxes", boxes.Count == 2);

                var remembered = await CallJsonAsync(cold, "remember_screen", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["frameId"] = frameId,
                    ["screenKey"] = "cu-v2-accept",
                    ["fingerprints"] = boxes.Select(b => BoxJson(b)).ToArray()
                });
                screenId = remembered.GetProperty("screenId").GetString();
                Check("remember_screen issued screenId", !string.IsNullOrWhiteSpace(screenId));

                var controlBox = boxes[0];
                var rememberedControl = await CallJsonAsync(cold, "remember_control", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["frameId"] = frameId,
                    ["screenId"] = screenId,
                    ["name"] = "accept-patch",
                    ["box"] = BoxJson(controlBox)
                });
                controlId = rememberedControl.GetProperty("controlId").GetString();
                Check("remember_control issued controlId", !string.IsNullOrWhiteSpace(controlId));

                var listed = await CallJsonAsync(cold, "list_remembered", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken
                });
                Check("list_remembered sees screen", listed.GetProperty("screens").EnumerateArray().Any(s => s.GetProperty("screenId").GetString() == screenId));
                Check("list_remembered sees control", listed.GetProperty("controls").EnumerateArray().Any(c => c.GetProperty("controlId").GetString() == controlId));
                CheckMemoryFiles();

                if (hostToken is not null)
                {
                    var hostShot = await ScreenshotAsync(cold, hostToken);
                    var hostFrame = hostShot.GetProperty("frameId").GetString();
                    var hostErr = await CallErrorAsync(cold, "remember_screen", new Dictionary<string, object?>
                    {
                        ["targetToken"] = hostToken,
                        ["frameId"] = hostFrame,
                        ["screenKey"] = "should-not-write",
                        ["fingerprints"] = boxes.Select(b => BoxJson(b)).ToArray()
                    });
                    Check("HostWindow remember_screen is host_window_forbidden", hostErr == "host_window_forbidden");

                    var hostClick = await CallErrorAsync(cold, "click_control", new Dictionary<string, object?>
                    {
                        ["targetToken"] = hostToken,
                        ["controlId"] = controlId
                    });
                    Check("HostWindow click_control is host_window_forbidden", hostClick == "host_window_forbidden");
                }
                else
                {
                    Console.WriteLine("SKIP HostWindow remember/click (stdio harness host is ComputerUse.Mcp.exe; no top-level HostWindow). Covered by RememberServiceTests / ClickControlServiceTests.");
                }
            }

            _screenshotCalls = 0;
            await using (var hot = await ConnectAsync(exe))
            {
                var list = await CallJsonAsync(hot, "list_windows", []);
                notepadToken = FindNotepadToken(list)!;
                var observed = await CallJsonAsync(hot, "observe_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken
                });
                Check("observe has no image payload marker in JSON text", !observed.ToString().Contains("data:image", StringComparison.OrdinalIgnoreCase));
                Check("observe visualized is false", observed.TryGetProperty("visualized", out var vis) && vis.ValueKind is JsonValueKind.False);
                Check("observe recognized screen", observed.GetProperty("screenId").GetString() == screenId);

                await CallJsonAsync(hot, "click_control", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["controlId"] = controlId
                });
                Check("hot-path screenshot_window count is 0 (repeat task does not screenshot)", _screenshotCalls == 0);
            }

            await using (var mismatch = await ConnectAsync(exe))
            {
                var list = await CallJsonAsync(mismatch, "list_windows", []);
                notepadToken = FindNotepadToken(list)!;
                var shot = await ScreenshotAsync(mismatch, notepadToken);
                await CallJsonAsync(mismatch, "operate_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["frameId"] = shot.GetProperty("frameId").GetString(),
                    ["actions"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "key", ["key"] = "A", ["modifiers"] = new[] { "Ctrl" } },
                        new Dictionary<string, object?> { ["type"] = "paste", ["value"] = new string('Q', 400) }
                    }
                });
                await CallJsonAsync(mismatch, "observe_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken
                });
                var code = await CallErrorAsync(mismatch, "click_control", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["controlId"] = controlId
                });
                Console.WriteLine("changed-content click_control code=" + code);
                Check("changed-content click is screen_mismatch", code == "screen_mismatch");
            }

            await using (var scale = await ConnectAsync(exe))
            {
                var list = await CallJsonAsync(scale, "list_windows", []);
                var notepadWindow = FindNotepadWindow(list);
                notepadToken = notepadWindow.GetProperty("targetToken").GetString()!;
                var hwnd = ParseHwnd(notepadWindow.GetProperty("hwnd").GetString()!);
                SetWindowPos(hwnd, 0, 80, 80, 240, 180, 0x0004);
                await Task.Delay(400);
                await CallJsonAsync(scale, "observe_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken
                });
                var code = await CallErrorAsync(scale, "click_control", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["controlId"] = controlId
                });
                Console.WriteLine("tiny-resize click_control code=" + code);
                Check("tiny-resize click is isError with an explicit code", !string.IsNullOrWhiteSpace(code));
            }

            await using (var forget = await ConnectAsync(exe))
            {
                var list = await CallJsonAsync(forget, "list_windows", []);
                notepadToken = FindNotepadToken(list)!;
                await CallJsonAsync(forget, "forget_controls", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken,
                    ["screenId"] = screenId
                });
                var observed = await CallJsonAsync(forget, "observe_window", new Dictionary<string, object?>
                {
                    ["targetToken"] = notepadToken
                });
                var forgottenId = observed.TryGetProperty("screenId", out var sid) ? sid : default;
                Check(
                    "observe after forget has null screenId",
                    forgottenId.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null);
            }
        }
        catch (Exception ex)
        {
            Failures.Add("unhandled: " + ex.Message);
            Console.Error.WriteLine(ex);
        }
        finally
        {
            try
            {
                if (notepad is { HasExited: false })
                    notepad.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
        }

        return Finish();
    }

    private static int Finish()
    {
        if (Failures.Count == 0)
        {
            Console.WriteLine("DESKTOP ACCEPTANCE: PASS");
            return 0;
        }

        Console.WriteLine("DESKTOP ACCEPTANCE: FAIL");
        foreach (var failure in Failures)
            Console.WriteLine(" - " + failure);
        return 1;
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok)
            Failures.Add(name);
    }

    private static async Task<McpClient> ConnectAsync(string exe)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "computer_use",
            Command = exe,
            StandardErrorLines = line => Console.Error.WriteLine(line)
        });
        return await McpClient.CreateAsync(transport);
    }

    private static async Task CheckToolsAsync(McpClient client)
    {
        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var required in RequiredTools)
            Check("tool registered: " + required, names.Contains(required));

        var operate = tools.First(t => t.Name == "operate_window");
        var desc = operate.Description ?? "";
        Check(
            "operate_window description mentions visualized/screenshot frame",
            desc.Contains("visualized", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("frame_not_visualized", StringComparison.OrdinalIgnoreCase)
            || desc.Contains("screenshot", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertV1ListShape(JsonElement list)
    {
        Check("list_windows has windows[]", list.TryGetProperty("windows", out _));
        Check("list_windows has contractVersion", list.TryGetProperty("contractVersion", out _));
        Check("list_windows limits omit memory quotas",
            list.TryGetProperty("limits", out var limits)
            && !limits.ToString().Contains("maxScreens", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonElement> ScreenshotAsync(McpClient client, string token)
    {
        _screenshotCalls++;
        return await CallJsonAsync(client, "screenshot_window", new Dictionary<string, object?> { ["targetToken"] = token });
    }

    private static async Task<(JsonElement Json, byte[] Png)> ScreenshotWithPngAsync(McpClient client, string token)
    {
        _screenshotCalls++;
        var result = await client.CallToolAsync("screenshot_window", new Dictionary<string, object?> { ["targetToken"] = token });
        Check("screenshot_window is not isError", result.IsError is not true);
        var json = ParsePayload(result);
        var png = result.Content.OfType<ImageContentBlock>().Select(DecodePng).FirstOrDefault();
        Check("screenshot_window returned PNG", png is { Length: > 0 });
        return (json, png ?? []);
    }

    private static byte[] DecodePng(ImageContentBlock image)
    {
        var raw = image.Data.ToArray();
        if (raw.Length >= 8 && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47)
            return raw;

        var asText = Encoding.UTF8.GetString(raw).Trim();
        try
        {
            var decoded = Convert.FromBase64String(asText);
            if (decoded.Length >= 8 && decoded[0] == 0x89)
                return decoded;
        }
        catch (FormatException)
        {
            // fall through
        }

        Console.Error.WriteLine("PNG payload prefix: " + Convert.ToHexString(raw.AsSpan(0, Math.Min(16, raw.Length))));
        return raw;
    }

    private static async Task<JsonElement> CallJsonAsync(McpClient client, string name, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(name, args);
        if (result.IsError is true)
        {
            var err = ParsePayload(result);
            var code = err.TryGetProperty("code", out var c) ? c.GetString() : "unknown";
            throw new InvalidOperationException($"{name} isError code={code} body={err}");
        }

        return ParsePayload(result);
    }

    private static async Task<string?> CallErrorAsync(McpClient client, string name, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(name, args);
        if (result.IsError is not true)
        {
            Failures.Add($"{name} expected isError, got success {ParsePayload(result)}");
            return null;
        }

        var err = ParsePayload(result);
        return err.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static JsonElement ParsePayload(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured)
            return structured;
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return default;
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? FindNotepadToken(JsonElement list) =>
        FindNotepadWindow(list).ValueKind == JsonValueKind.Undefined ? null : FindNotepadWindow(list).GetProperty("targetToken").GetString();

    private static JsonElement FindNotepadWindow(JsonElement list)
    {
        foreach (var window in list.GetProperty("windows").EnumerateArray())
        {
            var process = window.TryGetProperty("processName", out var p) ? p.GetString() : "";
            var className = window.TryGetProperty("className", out var c) ? c.GetString() : "";
            if (string.Equals(process, "notepad", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "Notepad", StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return default;
    }

    private static string? FindHostToken(JsonElement list)
    {
        foreach (var window in list.GetProperty("windows").EnumerateArray())
        {
            if (window.TryGetProperty("isHostWindow", out var host) && host.ValueKind is JsonValueKind.True)
                return window.GetProperty("targetToken").GetString();
        }

        return null;
    }

    private static List<Box> PickSpreadBoxes(byte[] png, int width, int height)
    {
        if (png.Length == 0)
            return [];

        using var ms = new MemoryStream(png);
        using var bmp = new Bitmap(ms);
        var boxSize = 32;
        var minVar = 40.0;
        var contentMinY = Math.Max(48, bmp.Height / 5);
        var scored = new List<(Box Box, double Var)>();
        for (var y = contentMinY; y + boxSize <= bmp.Height; y += 16)
        {
            for (var x = 0; x + boxSize <= bmp.Width; x += 16)
            {
                var variance = LumaVariance(bmp, x, y, boxSize, boxSize);
                if (variance >= minVar)
                    scored.Add((new Box(x, y, boxSize, boxSize), variance));
            }
        }

        scored.Sort((a, b) => b.Var.CompareTo(a.Var));
        var minDist = 0.25 * Math.Min(width, height);
        foreach (var first in scored)
        {
            foreach (var second in scored)
            {
                var dx = (first.Box.X + first.Box.Width / 2.0) - (second.Box.X + second.Box.Width / 2.0);
                var dy = (first.Box.Y + first.Box.Height / 2.0) - (second.Box.Y + second.Box.Height / 2.0);
                if (Math.Sqrt(dx * dx + dy * dy) >= minDist)
                    return [first.Box, second.Box];
            }
        }

        return scored.Count >= 2 ? [scored[0].Box, scored[1].Box] : [];
    }

    private static double LumaVariance(Bitmap bmp, int x, int y, int w, int h)
    {
        var n = w * h;
        if (n <= 1)
            return 0;
        double sum = 0, sumSq = 0;
        for (var row = 0; row < h; row++)
        {
            for (var col = 0; col < w; col++)
            {
                var c = bmp.GetPixel(x + col, y + row);
                var yL = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                sum += yL;
                sumSq += yL * yL;
            }
        }

        var mean = sum / n;
        return (sumSq / n) - (mean * mean);
    }

    private static Dictionary<string, object?> BoxJson(Box box) => new()
    {
        ["x"] = box.X,
        ["y"] = box.Y,
        ["width"] = box.Width,
        ["height"] = box.Height
    };

    private static void CheckMemoryFiles()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "computer-use-mcp",
            "memory");
        var png = Directory.Exists(root) && Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Length > 0;
        var json = Directory.Exists(root) && Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length > 0;
        Check("user memory dir has PNG", png);
        Check("user memory dir has JSON", json);

        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var tracked = Directory.Exists(repo)
            && Directory.GetFiles(repo, "*.png", SearchOption.AllDirectories)
                .Any(p => p.Contains($"{Path.DirectorySeparatorChar}memory{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Check("memory PNG is not inside the git work tree", !tracked);
    }

    private static nint ParseHwnd(string hex) =>
        (nint)long.Parse(hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private readonly record struct Box(int X, int Y, int Width, int Height);
}
