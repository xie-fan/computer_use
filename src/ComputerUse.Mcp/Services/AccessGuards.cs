using ComputerUse.Mcp.Abstractions;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Services;

internal static class AccessGuards
{
    public static void EnsureInteractive(ISessionGuard session)
    {
        switch (session.Evaluate())
        {
            case SessionDenial.SecureDesktop:
                throw new ComputerUseException(ErrorCodes.SecureDesktopForbidden, "Input desktop is not Default.");
            case SessionDenial.NotInteractive:
                throw new ComputerUseException(ErrorCodes.SessionNotInteractive, "The Windows session is not interactive.");
        }
    }

    public static Guid? EnsureCurrentDesktop(IVirtualDesktopMembership membership, nint hwnd)
    {
        var onCurrent = membership.IsOnCurrentVirtualDesktop(hwnd, out var id);
        if (onCurrent is false)
            throw new ComputerUseException(ErrorCodes.OffCurrentDesktop, "The window is not on the current VirtualDesktop.");
        if (onCurrent is null)
            throw new ComputerUseException(ErrorCodes.DesktopStateUnknown, "VirtualDesktop membership could not be determined.");
        return id;
    }

    public static void EnsureIntegrity(IProcessQuery processes, uint pid)
    {
        var target = processes.GetIntegrityLevel(pid);
        var self = processes.GetCurrentIntegrityLevel();
        if (target is IntegrityLevel.Unknown || self is IntegrityLevel.Unknown)
            return;
        if ((int)target > (int)self)
        {
            throw new ComputerUseException(
                ErrorCodes.IntegrityLevelBlocked,
                "The target process integrity level is higher than this process.");
        }
    }

    public static bool IntegrityBlocked(IProcessQuery processes, uint pid)
    {
        var target = processes.GetIntegrityLevel(pid);
        var self = processes.GetCurrentIntegrityLevel();
        return target is not IntegrityLevel.Unknown
            && self is not IntegrityLevel.Unknown
            && (int)target > (int)self;
    }

    public static bool ForegroundBelongsToTarget(IWindowQuery windows, nint foreground, nint target, uint pid)
    {
        if (foreground == 0 || target == 0)
            return false;
        var walk = foreground;
        for (var i = 0; i < 8; i++)
        {
            if (walk == target)
                return true;
            if (windows.GetAncestorRootOwner(walk) == target)
                return true;
            if (windows.GetAncestorRoot(walk) == target)
                return true;
            var owner = windows.GetOwner(walk);
            if (owner == 0 || owner == walk)
                break;
            walk = owner;
        }

        var rootOwner = windows.GetAncestorRootOwner(foreground);
        return rootOwner == target || (rootOwner != 0 && windows.GetPid(rootOwner) == pid && windows.GetOwner(rootOwner) == target);
    }

    public static bool HitIsAllowed(IWindowQuery windows, nint hit, nint target)
    {
        if (hit == 0 || target == 0)
            return false;
        var walk = hit;
        for (var i = 0; i < 8; i++)
        {
            if (walk == target)
                return true;
            if (windows.GetAncestorRoot(walk) == target)
                return true;
            if (windows.GetAncestorRootOwner(walk) == target)
                return true;
            var owner = windows.GetOwner(walk);
            if (owner == 0 || owner == walk)
                break;
            walk = owner;
        }
        return false;
    }
}
