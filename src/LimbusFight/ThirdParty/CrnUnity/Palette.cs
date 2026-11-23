// Vendored from AssetsTools.NET (MIT, nesrak1) at commit 790dae2; crunch decoder logic is zlib-licensed (Richard Geldreich, Binomial LLC). See THIRD-PARTY-NOTICES.md.
#nullable disable
#pragma warning disable
using AssetsTools.NET;
namespace LimbusFight.ThirdParty.CrnUnity;
public class Palette
{
    public uint Ofs;
    public uint Size;
    public ushort Num;

    public Palette(AssetsFileReader reader)
    {
        Ofs = reader.ReadUInt24();
        Size = reader.ReadUInt24();
        Num = reader.ReadUInt16();
    }
}
