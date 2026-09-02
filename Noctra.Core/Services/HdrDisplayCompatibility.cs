namespace Noctra.Services;

internal enum HdrTransferKind
{
    Unknown,
    Hdr10,
    Hlg,
    DolbyVision,
}

internal static class HdrDisplayCompatibility
{
    internal static bool IsNativeDisplaySupported(
        HdrTransferKind transfer,
        bool supportsHdr10,
        bool supportsHdr10Plus,
        bool supportsHlg,
        bool supportsDolbyVision)
        => transfer switch
        {
            HdrTransferKind.Hdr10 => supportsHdr10 || supportsHdr10Plus,
            HdrTransferKind.Hlg => supportsHlg,
            HdrTransferKind.DolbyVision => supportsDolbyVision,
            _ => true,
        };
}
