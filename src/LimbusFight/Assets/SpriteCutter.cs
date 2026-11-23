using System.Numerics;

namespace LimbusFight.Assets;

/// <summary>Packing flags stored in Unity's SpriteRenderData / SpriteAtlasData "settingsRaw".</summary>
public readonly record struct SpritePacking(uint Raw)
{
    /// <summary>True when the sprite lives on an atlas sheet.</summary>
    public bool Packed => (Raw & 1) != 0;

    /// <summary>True for tight packing (sprite outline is a polygon), false for rectangle packing.</summary>
    public bool Tight => ((Raw >> 1) & 1) == 0;

    /// <summary>0 none, 1 flip horizontal, 2 flip vertical, 3 rotate 180, 4 rotate 90.</summary>
    public int Rotation => (int)((Raw >> 2) & 0xF);

    public string Describe()
    {
        if (!Packed)
        {
            return "own texture";
        }

        string rotation = Rotation switch
        {
            0 => string.Empty,
            1 => ", flipped H",
            2 => ", flipped V",
            3 => ", rotated 180",
            4 => ", rotated 90",
            _ => $", rotation {Rotation}",
        };
        return (Tight ? "tight" : "rect") + rotation;
    }
}

/// <summary>
/// Everything needed to cut one sprite out of its texture. All rectangles are in Unity's
/// convention: pixels, origin at the bottom-left.
/// </summary>
public sealed class SpriteGeometry
{
    public string Name { get; init; } = string.Empty;

    /// <summary>m_Rect size: the sprite's full size as the artist made it.</summary>
    public float RectWidth { get; init; }

    public float RectHeight { get; init; }

    /// <summary>m_Pivot, 0..1 from the bottom-left of m_Rect.</summary>
    public float PivotX { get; init; } = 0.5f;

    public float PivotY { get; init; } = 0.5f;

    public float PixelsToUnits { get; init; } = 100f;

    /// <summary>Where the (trimmed) sprite sits on the texture / atlas sheet.</summary>
    public float TexRectX { get; init; }

    public float TexRectY { get; init; }

    public float TexRectWidth { get; init; }

    public float TexRectHeight { get; init; }

    /// <summary>Offset of the trimmed area inside m_Rect.</summary>
    public float TexRectOffsetX { get; init; }

    public float TexRectOffsetY { get; init; }

    public uint SettingsRaw { get; init; }

    /// <summary>Below 1 when the atlas sheet was stored smaller than the original art.</summary>
    public float DownscaleMultiplier { get; init; } = 1f;

    /// <summary>Mesh vertex positions in Unity units (x, y).</summary>
    public Vector2[] Positions { get; init; } = Array.Empty<Vector2>();

    /// <summary>Mesh UVs (0..1 over the whole texture), same count as <see cref="Positions"/>, or empty.</summary>
    public Vector2[] Uvs { get; init; } = Array.Empty<Vector2>();

    /// <summary>Triangle list, three vertex indices per triangle, or empty.</summary>
    public int[] Triangles { get; init; } = Array.Empty<int>();
}

/// <summary>A cut-out sprite: RGBA rows top-down, plus its pivot in pixels from the top-left.</summary>
public sealed record CutSprite(byte[] Rgba, int Width, int Height, float PivotX, float PivotY, string Method);

/// <summary>
/// Cuts single sprites out of decoded texture / atlas sheets. Pure pixel maths, no Unity or Raylib
/// types, so it can be tested on its own.
/// <para>
/// Order of preference: (1) draw the sprite's own mesh using its UVs, which is exactly what Unity does
/// and handles rotated and tightly packed sprites; (2) cut the rectangle out of the sheet, undo the
/// packing rotation, and for tight packing mask it with the mesh outline.
/// </para>
/// </summary>
public static class SpriteCutter
{
    public const int MaxSide = 8192;

    /// <param name="sheet">Decoded texture, RGBA, rows top-down (as <see cref="TextureDecoding.TryDecode"/> returns).</param>
    public static CutSprite Cut(SpriteGeometry g, byte[] sheet, int sheetWidth, int sheetHeight)
    {
        if (sheet.Length < (long)sheetWidth * sheetHeight * 4)
        {
            throw new InvalidDataException("Texture data is shorter than its size.");
        }

        var packing = new SpritePacking(g.SettingsRaw);
        bool rotated90 = packing.Packed && packing.Rotation == 4;

        // Upright size of the trimmed sprite, in original pixels.
        int outW = Side(rotated90 ? g.TexRectHeight : g.TexRectWidth);
        int outH = Side(rotated90 ? g.TexRectWidth : g.TexRectHeight);

        float pivotX = (g.PivotX * g.RectWidth) - g.TexRectOffsetX;
        float pivotYUp = (g.PivotY * g.RectHeight) - g.TexRectOffsetY;
        float pivotY = outH - pivotYUp;

        Vector2[] dest = DestinationPoints(g, outH);
        bool haveMesh = g.Positions.Length >= 3 && g.Triangles.Length >= 3 && IndicesValid(g.Triangles, g.Positions.Length);

        if (haveMesh && g.Uvs.Length == g.Positions.Length && UvsFitSheet(g, sheetWidth, sheetHeight) && PointsFit(dest, outW, outH))
        {
            byte[] drawn = DrawMesh(dest, g.Uvs, g.Triangles, sheet, sheetWidth, sheetHeight, outW, outH);
            return new CutSprite(drawn, outW, outH, pivotX, pivotY, "mesh");
        }

        byte[] upright = CropUpright(g, sheet, sheetWidth, sheetHeight, out int cropW, out int cropH);
        byte[] image = Orient(upright, cropW, cropH, packing, clockwise: true, out int w, out int h);

        if (!(packing.Tight && haveMesh && PointsFit(dest, w, h)))
        {
            return new CutSprite(image, w, h, pivotX, h - pivotYUp, "rect");
        }

        bool[] mask = Rasterize(dest, g.Triangles, w, h);
        double coverage = Coverage(image, mask);

        if (rotated90 && coverage < 0.98)
        {
            // Unity does not document which way "rotate 90" turns. Pick the direction whose
            // pixels line up with the sprite's own outline.
            byte[] other = Orient(upright, cropW, cropH, packing, clockwise: false, out int w2, out int h2);
            if (w2 == w && h2 == h)
            {
                double otherCoverage = Coverage(other, mask);
                if (otherCoverage > coverage)
                {
                    image = other;
                }
            }
        }

        ApplyMask(image, mask);
        return new CutSprite(image, w, h, pivotX, h - pivotYUp, "rect+mask");
    }

    // -----------------------------------------------------------------------------------------
    //  Mesh drawing
    // -----------------------------------------------------------------------------------------

    /// <summary>Mesh positions mapped to output pixels (top-down).</summary>
    private static Vector2[] DestinationPoints(SpriteGeometry g, int outH)
    {
        var points = new Vector2[g.Positions.Length];
        float ppu = g.PixelsToUnits > 0 ? g.PixelsToUnits : 100f;
        float shiftX = (g.PivotX * g.RectWidth) - g.TexRectOffsetX;
        float shiftY = (g.PivotY * g.RectHeight) - g.TexRectOffsetY;

        for (int i = 0; i < points.Length; i++)
        {
            float x = (g.Positions[i].X * ppu) + shiftX;
            float yUp = (g.Positions[i].Y * ppu) + shiftY;
            points[i] = new Vector2(x, outH - yUp);
        }

        return points;
    }

    private static bool IndicesValid(int[] triangles, int vertexCount)
    {
        if (triangles.Length % 3 != 0)
        {
            return false;
        }

        foreach (int index in triangles)
        {
            if ((uint)index >= (uint)vertexCount)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The mesh UVs must land inside the sprite's own area of the sheet, otherwise they are not trusted.</summary>
    private static bool UvsFitSheet(SpriteGeometry g, int sheetWidth, int sheetHeight)
    {
        float scale = g.DownscaleMultiplier > 0 ? g.DownscaleMultiplier : 1f;
        float left = (g.TexRectX * scale) - 1.5f;
        float bottom = (g.TexRectY * scale) - 1.5f;
        float right = ((g.TexRectX + g.TexRectWidth) * scale) + 1.5f;
        float top = ((g.TexRectY + g.TexRectHeight) * scale) + 1.5f;

        bool anyNonZero = false;
        foreach (Vector2 uv in g.Uvs)
        {
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                return false;
            }

            anyNonZero |= uv.X != 0 || uv.Y != 0;
            float x = uv.X * sheetWidth;
            float y = uv.Y * sheetHeight;
            if (x < left || x > right || y < bottom || y > top)
            {
                return false;
            }
        }

        return anyNonZero;
    }

    private static bool PointsFit(Vector2[] points, int width, int height)
    {
        foreach (Vector2 p in points)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < -1.5f || p.Y < -1.5f || p.X > width + 1.5f || p.Y > height + 1.5f)
            {
                return false;
            }
        }

        return points.Length > 0;
    }

    private static byte[] DrawMesh(Vector2[] dest, Vector2[] uvs, int[] triangles, byte[] sheet, int sheetW, int sheetH, int outW, int outH)
    {
        byte[] output = new byte[outW * outH * 4];

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];

            Vector2 s0 = SheetPoint(uvs[i0], sheetW, sheetH);
            Vector2 s1 = SheetPoint(uvs[i1], sheetW, sheetH);
            Vector2 s2 = SheetPoint(uvs[i2], sheetW, sheetH);

            ForEachPixel(dest[i0], dest[i1], dest[i2], outW, outH, (x, y, a, b, c) =>
            {
                Vector2 s = (s0 * a) + (s1 * b) + (s2 * c);
                int sx = Math.Clamp((int)MathF.Floor(s.X), 0, sheetW - 1);
                int sy = Math.Clamp((int)MathF.Floor(s.Y), 0, sheetH - 1);
                Buffer.BlockCopy(sheet, ((sy * sheetW) + sx) * 4, output, ((y * outW) + x) * 4, 4);
            });
        }

        return output;
    }

    /// <summary>UV (bottom-up, 0..1) to a pixel position on the top-down sheet.</summary>
    private static Vector2 SheetPoint(Vector2 uv, int sheetW, int sheetH) =>
        new(uv.X * sheetW, sheetH - (uv.Y * sheetH));

    private static bool[] Rasterize(Vector2[] dest, int[] triangles, int width, int height)
    {
        bool[] mask = new bool[width * height];
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            ForEachPixel(dest[triangles[t]], dest[triangles[t + 1]], dest[triangles[t + 2]], width, height,
                (x, y, _, _, _) => mask[(y * width) + x] = true);
        }

        return mask;
    }

    /// <summary>Calls <paramref name="visit"/> for every pixel whose centre is inside the triangle, with barycentric weights.</summary>
    private static void ForEachPixel(Vector2 p0, Vector2 p1, Vector2 p2, int width, int height, Action<int, int, float, float, float> visit)
    {
        float denom = ((p1.Y - p2.Y) * (p0.X - p2.X)) + ((p2.X - p1.X) * (p0.Y - p2.Y));
        if (MathF.Abs(denom) < 1e-6f)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        const float Eps = -1e-4f;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float a = (((p1.Y - p2.Y) * (px - p2.X)) + ((p2.X - p1.X) * (py - p2.Y))) / denom;
                float b = (((p2.Y - p0.Y) * (px - p2.X)) + ((p0.X - p2.X) * (py - p2.Y))) / denom;
                float c = 1f - a - b;
                if (a >= Eps && b >= Eps && c >= Eps)
                {
                    visit(x, y, a, b, c);
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    //  Rectangle cutting
    // -----------------------------------------------------------------------------------------

    /// <summary>Copies the sprite's area of the sheet as it is stored (not yet un-rotated), top-down, at original resolution.</summary>
    private static byte[] CropUpright(SpriteGeometry g, byte[] sheet, int sheetW, int sheetH, out int width, out int height)
    {
        width = Side(g.TexRectWidth);
        height = Side(g.TexRectHeight);
        float scale = g.DownscaleMultiplier > 0 ? g.DownscaleMultiplier : 1f;
        byte[] output = new byte[width * height * 4];

        for (int ty = 0; ty < height; ty++)
        {
            int rowFromBottom = height - 1 - ty;
            int sheetYUp = (int)MathF.Floor((g.TexRectY + rowFromBottom + 0.5f) * scale);
            int sy = sheetH - 1 - sheetYUp;
            if (sy < 0 || sy >= sheetH)
            {
                continue;
            }

            for (int tx = 0; tx < width; tx++)
            {
                int sx = (int)MathF.Floor((g.TexRectX + tx + 0.5f) * scale);
                if (sx < 0 || sx >= sheetW)
                {
                    continue;
                }

                Buffer.BlockCopy(sheet, ((sy * sheetW) + sx) * 4, output, ((ty * width) + tx) * 4, 4);
            }
        }

        return output;
    }

    /// <summary>Undoes the packing flip / rotation.</summary>
    private static byte[] Orient(byte[] src, int w, int h, SpritePacking packing, bool clockwise, out int outW, out int outH)
    {
        int rotation = packing.Packed ? packing.Rotation : 0;
        outW = w;
        outH = h;

        switch (rotation)
        {
            case 1:
                return Remap(src, w, h, w, h, (x, y) => (w - 1 - x, y));
            case 2:
                return Remap(src, w, h, w, h, (x, y) => (x, h - 1 - y));
            case 3:
                return Remap(src, w, h, w, h, (x, y) => (w - 1 - x, h - 1 - y));
            case 4:
                outW = h;
                outH = w;
                return clockwise
                    ? Remap(src, w, h, h, w, (x, y) => (y, h - 1 - x))
                    : Remap(src, w, h, h, w, (x, y) => (w - 1 - y, x));
            default:
                return src;
        }
    }

    /// <summary>Builds a new image where output pixel (x, y) comes from source pixel <paramref name="from"/>(x, y).</summary>
    private static byte[] Remap(byte[] src, int srcW, int srcH, int outW, int outH, Func<int, int, (int X, int Y)> from)
    {
        byte[] output = new byte[outW * outH * 4];
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                (int sx, int sy) = from(x, y);
                if ((uint)sx < (uint)srcW && (uint)sy < (uint)srcH)
                {
                    Buffer.BlockCopy(src, ((sy * srcW) + sx) * 4, output, ((y * outW) + x) * 4, 4);
                }
            }
        }

        return output;
    }

    /// <summary>Share of visible pixels that fall inside the mask (1.0 = all of them).</summary>
    private static double Coverage(byte[] rgba, bool[] mask)
    {
        long visible = 0;
        long inside = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (rgba[(i * 4) + 3] > 8)
            {
                visible++;
                if (mask[i])
                {
                    inside++;
                }
            }
        }

        return visible == 0 ? 1.0 : (double)inside / visible;
    }

    private static void ApplyMask(byte[] rgba, bool[] mask)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                rgba[(i * 4) + 0] = 0;
                rgba[(i * 4) + 1] = 0;
                rgba[(i * 4) + 2] = 0;
                rgba[(i * 4) + 3] = 0;
            }
        }
    }

    private static int Side(float value)
    {
        if (!float.IsFinite(value) || value < 0.5f)
        {
            throw new InvalidDataException("Sprite has an empty area on its texture.");
        }

        int side = (int)MathF.Round(value);
        if (side > MaxSide)
        {
            throw new InvalidDataException($"Sprite is too large ({side} px).");
        }

        return Math.Max(1, side);
    }
}
