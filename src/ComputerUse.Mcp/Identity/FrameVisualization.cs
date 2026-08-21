using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal static class FrameVisualization
{
    public static void EnsurePointerMayUse(FrameRecord frame, bool hasPointerActions)
    {
        if (!hasPointerActions)
            return;
        if (!frame.ImageReturnedToClient)
        {
            throw new ComputerUseException(
                ErrorCodes.FrameNotVisualized,
                "Pointer actions require a frame that was returned as an image to the client.");
        }
    }
}
