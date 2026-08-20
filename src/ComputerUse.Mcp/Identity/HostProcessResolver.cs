using ComputerUse.Mcp.Abstractions;

namespace ComputerUse.Mcp.Identity;

internal sealed class HostProcessResolver : IHostProcessResolver
{
    private readonly IProcessQuery _processes;
    private readonly object _gate = new();
    private HostIdentity? _host;
    private readonly Dictionary<uint, long> _hostTree = new();

    public HostProcessResolver(IProcessQuery processes)
    {
        _processes = processes;
        lock (_gate)
            RebuildUnlocked();
    }

    public HostIdentity? Host
    {
        get
        {
            lock (_gate)
                return _host;
        }
    }

    public void RefreshHostTree()
    {
        lock (_gate)
            RebuildUnlocked();
    }

    public bool IsHostProcess(uint pid)
    {
        if (!_processes.TryGetCreateTimeUtc(pid, out var create))
            return false;
        lock (_gate)
        {
            if (_hostTree.TryGetValue(pid, out var recorded) && recorded == create)
                return true;
            RebuildUnlocked();
            return _hostTree.TryGetValue(pid, out recorded) && recorded == create;
        }
    }

    public bool IsHostProcess(uint pid, long createTimeUtc)
    {
        lock (_gate)
            return _hostTree.TryGetValue(pid, out var recorded) && recorded == createTimeUtc;
    }

    private void RebuildUnlocked()
    {
        _hostTree.Clear();
        var parents = _processes.CaptureParentMap();
        _host = ResolveFromMap(parents);
        if (_host is null)
            return;

        var host = _host.Value;
        if (!_processes.TryGetCreateTimeUtc(host.Pid, out var hostCreate) || hostCreate != host.CreateTimeUtc)
        {
            _host = null;
            return;
        }

        var children = new Dictionary<uint, List<uint>>();
        foreach (var (pid, parent) in parents)
        {
            if (parent == 0 || parent == pid)
                continue;
            if (!children.TryGetValue(parent, out var list))
            {
                list = [];
                children[parent] = list;
            }
            list.Add(pid);
        }

        var queue = new Queue<uint>();
        var seen = new HashSet<uint>();
        queue.Enqueue(host.Pid);
        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();
            if (!seen.Add(pid))
                continue;
            if (!_processes.TryGetCreateTimeUtc(pid, out var create))
                continue;
            if (pid == host.Pid && create != host.CreateTimeUtc)
                continue;
            _hostTree[pid] = create;
            if (!children.TryGetValue(pid, out var kids))
                continue;
            foreach (var kid in kids)
                queue.Enqueue(kid);
        }
    }

    private HostIdentity? ResolveFromMap(IReadOnlyDictionary<uint, uint> parents)
    {
        uint? pid = null;
        var env = Environment.GetEnvironmentVariable("COMPUTER_USE_HOST_PID");
        if (uint.TryParse(env, out var parsed) && parsed != 0)
            pid = parsed;
        if (pid is null)
        {
            var self = NativeMethodsPid();
            if (self != 0 && parents.TryGetValue(self, out var parent) && parent != 0)
                pid = parent;
        }

        if (pid is null or 0)
            return null;
        if (!_processes.TryGetCreateTimeUtc(pid.Value, out var create))
            return null;
        var path = _processes.TryGetNormalizedImagePath(pid.Value);
        return new HostIdentity(pid.Value, create, path);
    }

    private static uint NativeMethodsPid()
    {
        try
        {
            return (uint)Environment.ProcessId;
        }
        catch
        {
            return 0;
        }
    }
}
