// Vendored from AssetsTools.NET (MIT, nesrak1) at commit 790dae2; crunch decoder logic is zlib-licensed (Richard Geldreich, Binomial LLC). See THIRD-PARTY-NOTICES.md.
#nullable disable
#pragma warning disable
namespace LimbusFight.ThirdParty.CrnUnity;
public enum Format
{
    Invalid = -1,

    DXT1 = 0,
    FirstValid = DXT1,

    DXT3 = 1,

    DXT5,

    DXT5_CCxY,
    DXT5_xGxR,
    DXT5_xGBR,
    DXT5_AGBR,

    DXN_XY,
    DXN_YX,

    DXT5A,

    ETC1,
    ETC2,
    ETC2A,
    ETC1S,
    ETC2AS,

    Total
}
