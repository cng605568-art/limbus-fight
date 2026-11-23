// Vendored from AssetsTools.NET (MIT, nesrak1) at commit 790dae2; crunch decoder logic is zlib-licensed (Richard Geldreich, Binomial LLC). See THIRD-PARTY-NOTICES.md.
#nullable disable
#pragma warning disable
using AssetsTools.NET;
using System.IO;

namespace LimbusFight.ThirdParty.CrnUnity;
public static class Info
{
    public static Header GetHeader(byte[] data)
    {
        if (data == null || data.Length < Consts.HEADER_MIN_SIZE)
            return null;

        var reader = new AssetsFileReader(new MemoryStream(data));
        reader.BigEndian = true;

        var fileHeader = new Header(reader);
        if (fileHeader.Sig != Consts.SIG_VALUE)
            return null;

        if (fileHeader.HeaderSize < Consts.HEADER_MIN_SIZE || data.Length < fileHeader.HeaderSize)
            return null;

        return fileHeader;
    }
}
