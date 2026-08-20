using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Identity;

internal static class CoordinateMapper
{
    public const string Rounding = "floor";

    public static ScreenPoint MapImageToScreen(FrameRecord frame, int imageX, int imageY)
    {
        if (imageX < 0 || imageY < 0 || imageX >= frame.Width || imageY >= frame.Height)
        {
            throw new ComputerUseException(
                ErrorCodes.InvalidAction,
                "Pointer coordinates are outside the half-open frame bounds [0,width)×[0,height).");
        }

        if (frame.Scale <= 0 || double.IsNaN(frame.Scale) || double.IsInfinity(frame.Scale))
        {
            throw new ComputerUseException(ErrorCodes.ActionFailed, "Frame scale is invalid.");
        }

        try
        {
            var sourceX = checked((int)Math.Floor(imageX / frame.Scale));
            var sourceY = checked((int)Math.Floor(imageY / frame.Scale));
            var screenX = checked(frame.CaptureOriginScreen.X + sourceX);
            var screenY = checked(frame.CaptureOriginScreen.Y + sourceY);
            return new ScreenPoint(screenX, screenY);
        }
        catch (OverflowException)
        {
            throw new ComputerUseException(ErrorCodes.InvalidAction, "Coordinate mapping overflowed.");
        }
    }
}
