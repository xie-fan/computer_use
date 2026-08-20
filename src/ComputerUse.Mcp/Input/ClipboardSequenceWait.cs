namespace ComputerUse.Mcp.Input;

internal static class ClipboardSequenceWait
{
    public static bool StillUnchanged(
        uint afterWrite,
        int restoreWaitMs,
        Func<uint> sequence,
        Action<int> sleep)
    {
        var waitUntil = Environment.TickCount64 + Math.Max(0, restoreWaitMs);
        while (true)
        {
            if (sequence() != afterWrite)
                return false;
            var remaining = waitUntil - Environment.TickCount64;
            if (remaining <= 0)
                return true;
            sleep((int)Math.Min(20, Math.Max(1, remaining)));
        }
    }
}
