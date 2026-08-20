using System.Diagnostics.CodeAnalysis;

namespace ComputerUse.Mcp.Capture;

internal sealed class TtlLruCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<TKey, Node> _map = new();
    private readonly LinkedList<TKey> _order = new();

    public TtlLruCache(int capacity, TimeSpan ttl, Func<DateTimeOffset>? now = null)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _ttl = ttl;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public List<TValue> EvictExpired()
    {
        var now = _now();
        var evicted = new List<TValue>();
        foreach (var key in _map.Keys.ToArray())
        {
            var node = _map[key];
            if (now - node.LastUsed <= _ttl)
                continue;
            evicted.Add(node.Value);
            _order.Remove(node.Link);
            _map.Remove(key);
        }
        return evicted;
    }

    public bool TryGet(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        if (_map.TryGetValue(key, out var node) && _now() - node.LastUsed <= _ttl)
        {
            Touch(key, node);
            value = node.Value;
            return true;
        }

        value = null;
        return false;
    }

    public List<TValue> Put(TKey key, TValue value)
    {
        var evicted = EvictExpired();
        if (_map.Remove(key, out var existing))
        {
            evicted.Add(existing.Value);
            _order.Remove(existing.Link);
        }

        while (_map.Count >= _capacity && _order.Last is { } last)
        {
            if (_map.Remove(last.Value, out var lru))
                evicted.Add(lru.Value);
            _order.RemoveLast();
        }

        var link = _order.AddFirst(key);
        _map[key] = new Node
        {
            Value = value,
            LastUsed = _now(),
            Link = link
        };
        return evicted;
    }

    public bool Remove(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        if (_map.Remove(key, out var node))
        {
            _order.Remove(node.Link);
            value = node.Value;
            return true;
        }

        value = null;
        return false;
    }

    public List<TValue> Drain()
    {
        var all = _map.Values.Select(node => node.Value).ToList();
        _map.Clear();
        _order.Clear();
        return all;
    }

    private void Touch(TKey key, Node node)
    {
        _order.Remove(node.Link);
        node.Link = _order.AddFirst(key);
        node.LastUsed = _now();
    }

    private sealed class Node
    {
        public required TValue Value { get; set; }
        public required DateTimeOffset LastUsed { get; set; }
        public required LinkedListNode<TKey> Link { get; set; }
    }
}
