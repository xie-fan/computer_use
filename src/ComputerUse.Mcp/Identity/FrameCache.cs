using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal sealed class FrameCache
{
    private readonly Limits _limits;
    private readonly object _gate = new();
    private readonly LinkedList<FrameRecord> _lru = new();
    private readonly Dictionary<string, LinkedListNode<FrameRecord>> _byId = new(StringComparer.Ordinal);

    public FrameCache(Limits limits)
    {
        _limits = limits;
    }

    public void Add(FrameRecord frame)
    {
        lock (_gate)
        {
            ExpireUnlocked(DateTimeOffset.UtcNow);
            if (_byId.TryGetValue(frame.FrameId, out var existing))
            {
                _lru.Remove(existing);
                _byId.Remove(frame.FrameId);
            }

            var node = _lru.AddFirst(frame);
            _byId[frame.FrameId] = node;
            while (_lru.Count > _limits.MaxCachedFrames)
            {
                var last = _lru.Last!;
                _byId.Remove(last.Value.FrameId);
                _lru.RemoveLast();
            }
        }
    }

    public FrameRecord Require(string frameId)
    {
        lock (_gate)
        {
            ExpireUnlocked(DateTimeOffset.UtcNow);
            if (!_byId.TryGetValue(frameId, out var node))
            {
                throw new ComputerUseException(
                    ErrorCodes.StaleCapture,
                    "The frameId is unknown or has expired.");
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value;
        }
    }

    public void EnsureMatchesToken(FrameRecord frame, TargetTokenPayload token)
    {
        if (frame.Hwnd != token.Hwnd
            || frame.Pid != token.Pid
            || frame.CreateTimeUtc != token.CreateTimeUtc
            || !string.Equals(frame.ClassName, token.ClassName, StringComparison.Ordinal))
        {
            throw new ComputerUseException(
                ErrorCodes.StaleTarget,
                "The frame does not belong to this target token.");
        }
    }

    public void EnsureGeometryIfPointer(FrameRecord frame, WindowGeometry live, bool hasPointerActions)
    {
        if (!hasPointerActions)
            return;
        if (frame.GeometryChanged(live, _limits.GeometryEpsilonPx))
        {
            throw new ComputerUseException(
                ErrorCodes.StaleCapture,
                "The window geometry or DPI has changed since this frame was captured.");
        }
    }

    private void ExpireUnlocked(DateTimeOffset now)
    {
        var node = _lru.Last;
        while (node is not null)
        {
            var prev = node.Previous;
            var age = now - node.Value.CapturedAt;
            if (age.TotalMilliseconds > _limits.FrameTtlMs)
            {
                _byId.Remove(node.Value.FrameId);
                _lru.Remove(node);
            }
            node = prev;
        }
    }
}
