using System.Text;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>Small text helpers on top of Raylib's built-in font (ASCII only).</summary>
public static class Ui
{
    private const string Ellipsis = "...";

    public static int Measure(string text, int size) => Raylib.MeasureText(text, size);

    public static void Text(string text, int x, int y, int size, Color color) =>
        Raylib.DrawText(ToAscii(text), x, y, size, color);

    public static void TextCentered(string text, int centerX, int y, int size, Color color)
    {
        string ascii = ToAscii(text);
        Raylib.DrawText(ascii, centerX - (Measure(ascii, size) / 2), y, size, color);
    }

    public static void TextRight(string text, int rightX, int y, int size, Color color)
    {
        string ascii = ToAscii(text);
        Raylib.DrawText(ascii, rightX - Measure(ascii, size), y, size, color);
    }

    /// <summary>Draws "label value" with a dim label; returns the x just after the value.</summary>
    public static int LabelValue(string label, string value, int x, int y, int size, Color valueColor)
    {
        string asciiLabel = ToAscii(label);
        string asciiValue = ToAscii(value);
        Raylib.DrawText(asciiLabel, x, y, size, Theme.TextDim);
        int valueX = x + Measure(asciiLabel, size) + 8;
        Raylib.DrawText(asciiValue, valueX, y, size, valueColor);
        return valueX + Measure(asciiValue, size);
    }

    /// <summary>Shortens text in the middle ("C:\Program...\Limbus Company") to fit a pixel width. Best for paths.</summary>
    public static string FitMiddle(string text, int size, int maxWidth)
    {
        string ascii = ToAscii(text);
        if (Measure(ascii, size) <= maxWidth)
        {
            return ascii;
        }

        int lo = 0;
        int hi = ascii.Length;
        string best = Ellipsis;

        while (lo <= hi)
        {
            int keep = (lo + hi) / 2;
            int head = (keep + 1) / 2;
            int tail = keep - head;
            string candidate = ascii[..head] + Ellipsis + ascii[(ascii.Length - tail)..];

            if (Measure(candidate, size) <= maxWidth)
            {
                best = candidate;
                lo = keep + 1;
            }
            else
            {
                hi = keep - 1;
            }
        }

        return best;
    }

    /// <summary>Shortens text at the end ("Steam reports an upd...") to fit a pixel width.</summary>
    public static string FitEnd(string text, int size, int maxWidth)
    {
        string ascii = ToAscii(text);
        if (Measure(ascii, size) <= maxWidth)
        {
            return ascii;
        }

        int lo = 0;
        int hi = ascii.Length;
        string best = Ellipsis;

        while (lo <= hi)
        {
            int keep = (lo + hi) / 2;
            string candidate = ascii[..keep].TrimEnd() + Ellipsis;

            if (Measure(candidate, size) <= maxWidth)
            {
                best = candidate;
                lo = keep + 1;
            }
            else
            {
                hi = keep - 1;
            }
        }

        return best;
    }

    /// <summary>Raylib's default font only covers ASCII; anything else is shown as '?'.</summary>
    public static string ToAscii(string text)
    {
        bool clean = true;
        foreach (char c in text)
        {
            if (c > 126 || (c < 32 && c != '\n'))
            {
                clean = false;
                break;
            }
        }

        if (clean)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c > 126 || (c < 32 && c != '\n') ? '?' : c);
        }

        return sb.ToString();
    }
}
