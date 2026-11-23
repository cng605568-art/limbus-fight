// Vendored from AssetsTools.NET (MIT, nesrak1) at commit 790dae2; crunch decoder logic is zlib-licensed (Richard Geldreich, Binomial LLC). See THIRD-PARTY-NOTICES.md.
#nullable disable
#pragma warning disable
namespace LimbusFight.ThirdParty.CrnUnity;
internal struct BlockBufferElement
{
    public ushort EndpointReference;
    public ushort ColorEndpointIndex;
    public ushort Alpha0EndpointIndex;
    public ushort Alpha1EndpointIndex;
}
