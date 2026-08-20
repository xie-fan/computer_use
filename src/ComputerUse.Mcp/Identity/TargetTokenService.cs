using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal sealed class TargetTokenService
{
    internal const int MaxRevoked = 1024;
    internal const int MaxIssued = 1024;
    internal static readonly TimeSpan RevokedTtl = TimeSpan.FromMinutes(30);

    private readonly byte[] _key;
    private readonly HMACSHA256 _hmac;
    private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _revokedAt = new(StringComparer.Ordinal);
    private readonly Queue<string> _revokedOrder = new();
    private readonly Dictionary<IssuedKey, IssuedEntry> _issued = [];
    private readonly object _gate = new();
    private int _issuedGeneration;

    public TargetTokenService()
    {
        _key = RandomNumberGenerator.GetBytes(32);
        _hmac = new HMACSHA256(_key);
    }

    public void BeginIssuedSnapshot()
    {
        lock (_gate)
            _issuedGeneration++;
    }

    public void EndIssuedSnapshot()
    {
        lock (_gate)
        {
            if (_issued.Count == 0)
                return;
            var stale = _issued.Where(kv => kv.Value.Generation != _issuedGeneration).Select(kv => kv.Key).ToList();
            foreach (var key in stale)
                _issued.Remove(key);
        }
    }

    public string Issue(nint hwnd, uint pid, long createTimeUtc, string className)
    {
        className ??= "";
        var key = new IssuedKey(hwnd, pid, createTimeUtc, className);
        lock (_gate)
        {
            PurgeRevokedUnlocked(DateTimeOffset.UtcNow);
            if (_issued.TryGetValue(key, out var existing)
                && !_revoked.Contains(existing.Token))
            {
                existing.Generation = _issuedGeneration;
                return existing.Token;
            }

            var token = MintUnlocked(hwnd, pid, createTimeUtc, className);
            _issued[key] = new IssuedEntry(token, _issuedGeneration);
            while (_issued.Count > MaxIssued)
            {
                var oldest = _issued.Keys.First();
                _issued.Remove(oldest);
            }

            return token;
        }
    }

    public bool TryDecode(string token, out TargetTokenPayload payload)
    {
        lock (_gate)
            return TryDecodeUnlocked(token, out payload);
    }

    public TargetTokenPayload RequireValid(string token, IWindowQuery windows, IProcessQuery processes)
    {
        TargetTokenPayload payload;
        lock (_gate)
        {
            PurgeRevokedUnlocked(DateTimeOffset.UtcNow);
            if (_revoked.Contains(token))
                throw Stale();
            if (!TryDecodeUnlocked(token, out payload))
                throw Stale();
        }

        if (!MatchesLive(payload, windows, processes))
            throw Stale();
        return payload;
    }

    public static bool MatchesLive(TargetTokenPayload token, IWindowQuery windows, IProcessQuery processes)
    {
        if (!windows.IsWindow(token.Hwnd))
            return false;
        if (windows.GetPid(token.Hwnd) != token.Pid)
            return false;
        if (!string.Equals(windows.GetClassName(token.Hwnd), token.ClassName, StringComparison.Ordinal))
            return false;
        return processes.TryGetCreateTimeUtc(token.Pid, out var createTime) && createTime == token.CreateTimeUtc;
    }

    public void Revoke(string token)
    {
        lock (_gate)
        {
            PurgeRevokedUnlocked(DateTimeOffset.UtcNow);
            if (!_revoked.Add(token))
                return;
            _revokedAt[token] = DateTimeOffset.UtcNow;
            _revokedOrder.Enqueue(token);
            foreach (var dead in _issued.Where(kv => kv.Value.Token == token).Select(kv => kv.Key).ToList())
                _issued.Remove(dead);
            PurgeRevokedUnlocked(DateTimeOffset.UtcNow);
        }
    }

    public static string FormatHwnd(nint hwnd) =>
        "0x" + unchecked((ulong)hwnd).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    private string MintUnlocked(nint hwnd, uint pid, long createTimeUtc, string className)
    {
        var classBytes = Encoding.UTF8.GetBytes(className);
        if (classBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("Class name is too long to encode.");

        var issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> payload = stackalloc byte[8 + 8 + 4 + 8 + 8 + 2 + classBytes.Length];
        var w = payload;
        BinaryPrimitives.WriteUInt64LittleEndian(w, 1);
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

        Span<byte> mac = stackalloc byte[32];
        ComputeMacUnlocked(payload, mac);
        return "cu1." + Base64Url(payload) + "." + Base64Url(mac);
    }

    private bool TryDecodeUnlocked(string token, out TargetTokenPayload payload)
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

        Span<byte> expected = stackalloc byte[32];
        ComputeMacUnlocked(data, expected);
        if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(expected, mac))
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

    private void ComputeMacUnlocked(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        if (!_hmac.TryComputeHash(payload, destination, out var written) || written != destination.Length)
            throw new CryptographicException("HMACSHA256 failed.");
    }

    private void PurgeRevokedUnlocked(DateTimeOffset now)
    {
        while (_revokedOrder.Count > 0)
        {
            var oldest = _revokedOrder.Peek();
            if (!_revokedAt.TryGetValue(oldest, out var at))
            {
                _revokedOrder.Dequeue();
                _revoked.Remove(oldest);
                continue;
            }

            if (_revoked.Count > MaxRevoked || now - at > RevokedTtl)
            {
                _revokedOrder.Dequeue();
                _revoked.Remove(oldest);
                _revokedAt.Remove(oldest);
                continue;
            }

            break;
        }
    }

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

    private readonly record struct IssuedKey(nint Hwnd, uint Pid, long CreateTimeUtc, string ClassName);

    private sealed class IssuedEntry(string token, int generation)
    {
        public string Token { get; } = token;
        public int Generation { get; set; } = generation;
    }
}
