using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal sealed class TargetTokenService
{
    internal const int MaxRevoked = 1024;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);
    private readonly Queue<string> _revokedOrder = new();
    private readonly object _gate = new();

    public string Issue(nint hwnd, uint pid, long createTimeUtc, string className)
    {
        className ??= "";
        var classBytes = Encoding.UTF8.GetBytes(className);
        if (classBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("Class name is too long to encode.");

        var issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> payload = stackalloc byte[8 + 8 + 4 + 8 + 8 + 2 + classBytes.Length];
        var w = payload;
        BinaryPrimitives.WriteUInt64LittleEndian(w, 1); // version
        w = w[8..];
        BinaryPrimitives.WriteInt64LittleEndian(w, hwnd);
        w = w[8..];
        BinaryPrimitives.WriteUInt32LittleEndian(w, pid);
        w = w[4..];
        BinaryPrimitives.WriteInt64LittleEndian(w, createTimeUtc);
        w = w[8..];
        BinaryPrimitives.WriteInt64LittleEndian(w, issued);
        w = w[8..];
        BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)classBytes.Length);
        w = w[2..];
        classBytes.CopyTo(w);

        var mac = HMACSHA256.HashData(_key, payload);
        return "cu1." + Base64Url(payload) + "." + Base64Url(mac);
    }

    public bool TryDecode(string token, out TargetTokenPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != "cu1")
            return false;
        byte[] data;
        byte[] mac;
        try
        {
            data = FromBase64Url(parts[1]);
            mac = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, data);
        if (!CryptographicOperations.FixedTimeEquals(expected, mac))
            return false;
        if (data.Length < 8 + 8 + 4 + 8 + 8 + 2)
            return false;

        var r = data.AsSpan();
        var version = BinaryPrimitives.ReadUInt64LittleEndian(r);
        if (version != 1)
            return false;
        r = r[8..];
        var hwnd = (nint)BinaryPrimitives.ReadInt64LittleEndian(r);
        r = r[8..];
        var pid = BinaryPrimitives.ReadUInt32LittleEndian(r);
        r = r[4..];
        var createTime = BinaryPrimitives.ReadInt64LittleEndian(r);
        r = r[8..];
        var issued = BinaryPrimitives.ReadInt64LittleEndian(r);
        r = r[8..];
        var classLen = BinaryPrimitives.ReadUInt16LittleEndian(r);
        r = r[2..];
        if (r.Length != classLen)
            return false;
        var className = Encoding.UTF8.GetString(r);

        payload = new TargetTokenPayload
        {
            Hwnd = hwnd,
            Pid = pid,
            CreateTimeUtc = createTime,
            ClassName = className,
            IssuedUnixMs = issued
        };
        return true;
    }

    public TargetTokenPayload RequireValid(string token, Abstractions.IWindowQuery windows, Abstractions.IProcessQuery processes)
    {
        lock (_gate)
        {
            if (_revoked.Contains(token))
                throw Stale();
        }

        if (!TryDecode(token, out var payload))
            throw Stale();

        if (!windows.IsWindow(payload.Hwnd))
            throw Stale();
        if (windows.GetPid(payload.Hwnd) != payload.Pid)
            throw Stale();
        var className = windows.GetClassName(payload.Hwnd);
        if (!string.Equals(className, payload.ClassName, StringComparison.Ordinal))
            throw Stale();
        if (!processes.TryGetCreateTimeUtc(payload.Pid, out var createTime) || createTime != payload.CreateTimeUtc)
            throw Stale();
        return payload;
    }

    public void Revoke(string token)
    {
        lock (_gate)
        {
            if (!_revoked.Add(token))
                return;
            _revokedOrder.Enqueue(token);
            while (_revoked.Count > MaxRevoked)
            {
                var oldest = _revokedOrder.Dequeue();
                _revoked.Remove(oldest);
            }
        }
    }

    public static string FormatHwnd(nint hwnd) =>
        "0x" + unchecked((ulong)hwnd).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    private static Domain.ComputerUseException Stale() =>
        new(Domain.ErrorCodes.StaleTarget, "The target token no longer matches a live window identity.");

    private static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
