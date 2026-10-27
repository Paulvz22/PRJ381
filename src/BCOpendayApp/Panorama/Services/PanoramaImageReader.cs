using System.Text;

namespace BCOpendayApp.Panorama.Services;

public enum PanoramaImageFormat
{
    Jpeg,
    Png,
}

/// <summary>
/// Reads the pixel dimensions from the header of a JPEG or PNG stream. It deliberately does not decode the
/// whole image, so importing a large panorama does not create a second full-resolution copy in memory.
/// </summary>
public static class PanoramaImageReader
{
    public static bool TryReadDimensions(
        Stream stream,
        out PanoramaImageFormat format,
        out int width,
        out int height)
    {
        format = default;
        width = 0;
        height = 0;

        if (stream is null || !stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        try
        {
            stream.Position = 0;
            var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var first = reader.ReadBytes(8);
            if (first.Length == 8 && IsPng(first))
            {
                return TryReadPng(reader, out format, out width, out height);
            }

            if (first.Length >= 2 && first[0] == 0xFF && first[1] == 0xD8)
            {
                stream.Position = 2;
                return TryReadJpeg(reader, out format, out width, out height);
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadPng(
        BinaryReader reader,
        out PanoramaImageFormat format,
        out int width,
        out int height)
    {
        format = PanoramaImageFormat.Png;
        width = 0;
        height = 0;

        var length = ReadUInt32BigEndian(reader);
        var type = reader.ReadBytes(4);
        if (length != 13 || type.Length != 4 || type[0] != (byte)'I' || type[1] != (byte)'H'
            || type[2] != (byte)'D' || type[3] != (byte)'R')
        {
            return false;
        }

        var parsedWidth = ReadUInt32BigEndian(reader);
        var parsedHeight = ReadUInt32BigEndian(reader);
        if (parsedWidth == 0 || parsedHeight == 0 || parsedWidth > int.MaxValue || parsedHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)parsedWidth;
        height = (int)parsedHeight;
        return true;
    }

    private static bool TryReadJpeg(
        BinaryReader reader,
        out PanoramaImageFormat format,
        out int width,
        out int height)
    {
        format = PanoramaImageFormat.Jpeg;
        width = 0;
        height = 0;

        var stream = reader.BaseStream;
        while (stream.Position < stream.Length)
        {
            if (reader.ReadByte() != 0xFF)
            {
                // Bytes outside a marker are not valid in the JPEG header area. Keep scanning for the next
                // marker so harmless padding does not reject an otherwise readable image.
                continue;
            }

            var marker = reader.ReadByte();
            while (marker == 0xFF)
            {
                marker = reader.ReadByte();
            }

            if (marker == 0xD8)
            {
                continue;
            }

            if (marker == 0xD9 || marker == 0xDA)
            {
                // End of image or start of scan reached without a start-of-frame marker.
                return false;
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                // Markers without a length.
                continue;
            }

            var length = ReadUInt16BigEndian(reader);
            if (length < 2)
            {
                return false;
            }

            var payloadLength = length - 2;
            if (payloadLength > stream.Length - stream.Position)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (payloadLength < 5)
                {
                    return false;
                }

                _ = reader.ReadByte(); // sample precision
                var parsedHeight = ReadUInt16BigEndian(reader);
                var parsedWidth = ReadUInt16BigEndian(reader);
                if (parsedWidth == 0 || parsedHeight == 0)
                {
                    return false;
                }

                width = parsedWidth;
                height = parsedHeight;
                return true;
            }

            stream.Position += payloadLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static bool IsPng(byte[] bytes) => bytes.Length >= 8
        && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
        && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static int ReadUInt16BigEndian(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (high << 8) | low;
    }

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        uint value = 0;
        for (var i = 0; i < 4; i++)
        {
            value = (value << 8) | reader.ReadByte();
        }

        return value;
    }
}
