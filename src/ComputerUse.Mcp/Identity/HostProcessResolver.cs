using ComputerUse.Mcp.Abstractions;

namespace ComputerUse.Mcp.Identity;

internal sealed class HostProcessResolver : IHostProcessResolver
{
    private readonly IProcessQuery _processes;
    private readonly HostIdentity? _host;

    public HostProcessResolver(IProcessQuery processes)
    {
        _processes = processes;
        _host = Resolve();
    }

    public HostIdentity? Host => _host;

    public bool IsHostProcess(uint pid)
    {
        if (_host is null)
            return false;
        return BelongsToHostTree(pid);
    }

    private HostIdentity? Resolve()
    {
        uint? pid = null;
        var env = Environment.GetEnvironmentVariable("COMPUTER_USE_HOST_PID");
        if (uint.TryParse(env, out var parsed) && parsed != 0)
            pid = parsed;
        pid ??= _processes.TryGetParentPid(NativeMethodsPid());

        if (pid is null or 0)
            return null;
        if (!_processes.TryGetCreateTimeUtc(pid.Value, out var create))
            return null;
        var path = _processes.TryGetNormalizedImagePath(pid.Value);
        return new HostIdentity(pid.Value, create, path);
    }

    private bool BelongsToHostTree(uint pid)
    {
        var host = _host!.Value;
        var current = pid;
        for (var i = 0; i < 32; i++)
        {
            if (!_processes.TryGetCreateTimeUtc(current, out var create))
                return false;
            if (current == host.Pid)
                return create == host.CreateTimeUtc;
            var parent = _processes.TryGetParentPid(current);
            if (parent is null or 0 || parent == current)
                return false;
            if (_processes.TryGetCreateTimeUtc(parent.Value, out var parentCreate) && parentCreate > create)
                return false;
            current = parent.Value;
        }
        return false;
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
