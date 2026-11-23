using AssetRipper.TextureDecoder.Astc;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Dxt;
using AssetRipper.TextureDecoder.Etc;
using AssetRipper.TextureDecoder.Rgb;
using AssetRipper.TextureDecoder.Rgb.Formats;
using Crn = LimbusFight.ThirdParty.CrnUnity;

namespace LimbusFight.Assets;

/// <summary>
/// Turns Unity Texture2D pixel data into plain RGBA (4 bytes per pixel, top row first).
/// Covers uncompressed, DXT / BC (PC), ETC / EAC / ASTC (mobile) and Crunch-compressed textures.
/// </summary>
public static class TextureDecoding
{
    /// <summary>Largest texture side Limbus Fight will decode (protects against corrupt sizes).</summary>
    public const int MaxSide = 8192;

    /// <summary>Unity's TextureFormat enum value to its name (only the formats worth naming).</summary>
    public static string FormatName(int format) => format switch
    {
        1 => "Alpha8",
        2 => "ARGB4444",
        3 => "RGB24",
        4 => "RGBA32",
        5 => "ARGB32",
        7 => "RGB565",
        9 => "R16",
        10 => "DXT1",
        12 => "DXT5",
        13 => "RGBA4444",
        14 => "BGRA32",
        15 => "RHalf",
        16 => "RGHalf",
        17 => "RGBAHalf",
        18 => "RFloat",
        19 => "RGFloat",
        20 => "RGBAFloat",
        24 => "BC6H",
        25 => "BC7",
        26 => "BC4",
        27 => "BC5",
        28 => "DXT1Crunched",
        29 => "DXT5Crunched",
        34 => "ETC_RGB4",
        41 => "EAC_R",
        42 => "EAC_R_SIGNED",
        43 => "EAC_RG",
        44 => "EAC_RG_SIGNED",
        45 => "ETC2_RGB",
        46 => "ETC2_RGBA1",
        47 => "ETC2_RGBA8",
        48 or 54 => "ASTC_4x4",
        49 or 55 => "ASTC_5x5",
        50 or 56 => "ASTC_6x6",
        51 or 57 => "ASTC_8x8",
        52 or 58 => "ASTC_10x10",
        53 or 59 => "ASTC_12x12",
        60 => "ETC_RGB4_3DS",
        62 => "RG16",
        63 => "R8",
        64 => "ETC_RGB4Crunched",
        65 => "ETC2_RGBA8Crunched",
        _ => $"Format{format}",
    };

    public static bool IsCrunched(int format) => format is 28 or 29 or 64 or 65;

    /// <summary>
    /// Decodes <paramref name="data"/> (mip level 0 first, as Unity stores it) into top-down RGBA.
    /// Returns false with a reason when the format is not supported or the data is too short.
    /// </summary>
    public static bool TryDecode(int format, byte[] data, int width, int height, out byte[] rgba, out string error)
    {
        rgba = Array.Empty<byte>();
        error = string.Empty;

        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
        {
            error = $"Invalid size {width}x{height}";
            return false;
        }

        if (IsCrunched(format))
        {
            if (!TryUncrunch(data, format, width, height, out data, out format, out error))
            {
                return false;
            }
        }

        long needed = RequiredBytes(format, width, height);
        if (needed < 0)
        {
            error = $"{FormatName(format)} is not supported yet";
            return false;
        }

        if (data.Length < needed)
        {
            error = $"Pixel data too short ({data.Length} of {needed} bytes)";
            return false;
        }

        try
        {
            byte[] output;
            switch (format)
            {
                case 1: RgbConverter.Convert<ColorA<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 2: RgbConverter.Convert<ColorARGB16, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 3: RgbConverter.Convert<ColorRGB<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 4: output = data.AsSpan(0, width * height * 4).ToArray(); break;
                case 5: RgbConverter.Convert<ColorARGB<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 7: RgbConverter.Convert<ColorRGB16, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 9: RgbConverter.Convert<ColorR<ushort>, ushort, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 10: DxtDecoder.DecompressDXT1<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 12: DxtDecoder.DecompressDXT5<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 13: RgbConverter.Convert<ColorRGBA16, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 14: RgbConverter.Convert<ColorBGRA<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 15: RgbConverter.Convert<ColorR<Half>, Half, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 16: RgbConverter.Convert<ColorRG<Half>, Half, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 17: RgbConverter.Convert<ColorRGBA<Half>, Half, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 18: RgbConverter.Convert<ColorR<float>, float, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 19: RgbConverter.Convert<ColorRG<float>, float, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 20: RgbConverter.Convert<ColorRGBA<float>, float, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 24: Bc6h.Decompress<ColorRGBA<byte>, byte>(data, width, height, false, out output); break;
                case 25: Bc7.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 26: Bc4.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 27: Bc5.Decompress<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 34 or 60: EtcDecoder.DecompressETC<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 41: EtcDecoder.DecompressEACRUnsigned<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 42: EtcDecoder.DecompressEACRSigned<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 43: EtcDecoder.DecompressEACRGUnsigned<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 44: EtcDecoder.DecompressEACRGSigned<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 45: EtcDecoder.DecompressETC2<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 46: EtcDecoder.DecompressETC2A1<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 47: EtcDecoder.DecompressETC2A8<ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case >= 48 and <= 59:
                    int block = AstcBlockSize(format);
                    AstcDecoder.DecodeASTC<ColorRGBA<byte>, byte>(data, width, height, block, block, out output);
                    break;
                case 62: RgbConverter.Convert<ColorRG<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                case 63: RgbConverter.Convert<ColorR<byte>, byte, ColorRGBA<byte>, byte>(data, width, height, out output); break;
                default:
                    error = $"{FormatName(format)} is not supported yet";
                    return false;
            }

            FlipVertically(output, width, height);
            rgba = output;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException or NotSupportedException)
        {
            error = $"Decoding {FormatName(format)} failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Bytes needed for mip level 0, or -1 for unsupported formats.</summary>
    public static long RequiredBytes(int format, int width, int height)
    {
        long pixels = (long)width * height;
        long blocks = (long)((width + 3) / 4) * ((height + 3) / 4);

        return format switch
        {
            1 or 63 => pixels,
            2 or 7 or 9 or 13 or 15 or 62 => pixels * 2,
            3 => pixels * 3,
            4 or 5 or 14 or 16 or 18 => pixels * 4,
            17 or 19 => pixels * 8,
            20 => pixels * 16,
            10 or 26 or 34 or 41 or 42 or 45 or 46 or 60 => blocks * 8,
            12 or 24 or 25 or 27 or 43 or 44 or 47 => blocks * 16,
            >= 48 and <= 59 => AstcBytes(format, width, height),
            _ => -1,
        };
    }

    private static int AstcBlockSize(int format) => ((format - 48) % 6) switch
    {
        0 => 4,
        1 => 5,
        2 => 6,
        3 => 8,
        4 => 10,
        _ => 12,
    };

    private static long AstcBytes(int format, int width, int height)
    {
        int block = AstcBlockSize(format);
        return (long)((width + block - 1) / block) * ((height + block - 1) / block) * 16;
    }

    /// <summary>
    /// Crunch-compressed textures hold a .crn file; unpacking mip 0 gives ordinary DXT / ETC blocks.
    /// </summary>
    private static bool TryUncrunch(byte[] crn, int crunchedFormat, int width, int height, out byte[] blocks, out int blockFormat, out string error)
    {
        blocks = Array.Empty<byte>();
        error = string.Empty;
        blockFormat = crunchedFormat switch
        {
            28 => 10, // DXT1
            29 => 12, // DXT5
            64 => 34, // ETC_RGB4
            _ => 47,  // ETC2_RGBA8
        };

        try
        {
            Crn.TextureInfo? info = Crn.Decomp.GetTextureInfo(crn);
            Crn.Unpacker? context = info is null ? null : Crn.Decomp.UnpackBegin(crn);
            if (info is null || context is null)
            {
                error = "Crunch data could not be read";
                return false;
            }

            uint blocksX = (Math.Max(1u, info.Width) + 3) >> 2;
            uint blocksY = (Math.Max(1u, info.Height) + 3) >> 2;
            uint rowPitch = blocksX * info.BytesPerBlock;
            byte[][] faces = new byte[Math.Max(1u, info.Faces)][];
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = new byte[blocksY * rowPitch];
            }

            bool ok = Crn.Decomp.UnpackLevel(context, faces, rowPitch, 0);
            Crn.Decomp.UnpackEnd(context);

            if (!ok)
            {
                error = "Crunch data could not be unpacked";
                return false;
            }

            blocks = faces[0];
            return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidOperationException or OverflowException)
        {
            error = "Crunch unpacking failed: " + ex.Message;
            return false;
        }
    }

    /// <summary>Unity stores rows bottom-up; screens and PNGs expect top-down.</summary>
    private static void FlipVertically(byte[] rgba, int width, int height)
    {
        int stride = width * 4;
        byte[] row = new byte[stride];

        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Span<byte> a = rgba.AsSpan(top * stride, stride);
            Span<byte> b = rgba.AsSpan(bottom * stride, stride);
            a.CopyTo(row);
            b.CopyTo(a);
            row.AsSpan().CopyTo(b);
        }
    }
}
