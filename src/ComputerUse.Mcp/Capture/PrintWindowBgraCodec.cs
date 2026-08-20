using System.Buffers.Binary;
using ComputerUse.Mcp.Domain;

namespace ComputerUse.Mcp.Capture;

internal static class PrintWindowBgraCodec
{
    public static ReadOnlySpan<byte> Magic => "CUBG"u8;
    public const int HeaderBytes = 16;
    public const int MaxDimension = 16_384;
    public const int MaxPayloadBytes = 64 * 1024 * 1024;

    public static void Write(Stream stream, CapturedBitmap frame)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], frame.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], frame.Stride);
        stream.Write(header);
        stream.Write(frame.Bgra.AsSpan(0, frame.ByteLength));
        stream.Flush();
    }

    public static CapturedBitmap Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        ReadExact(stream, header);
        if (!header[..4].SequenceEqual(Magic))
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper returned an invalid frame.");

        var width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        var stride = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            || stride < width * 4)
        {
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper returned an invalid frame size.");
        }

        var payload = checked((long)stride * height);
        if (payload > MaxPayloadBytes)
            throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper returned an oversized frame.");

        CapturedBitmap? captured = CapturedBitmap.Rent(width, height, stride, "print_window");
        try
        {
            ReadExact(stream, captured.Bgra.AsSpan(0, captured.ByteLength));
            var result = captured;
            captured = null;
            return result;
        }
        finally
        {
            captured?.Return();
        }
    }

    private static void ReadExact(Stream stream, Span<byte> dest)
    {
        var offset = 0;
        while (offset < dest.Length)
        {
            var n = stream.Read(dest[offset..]);
            if (n == 0)
                throw new ComputerUseException(ErrorCodes.CaptureFailed, "PrintWindow helper closed the frame stream early.");
            offset += n;
        }
    }
}
