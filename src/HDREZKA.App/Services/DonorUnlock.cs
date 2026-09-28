using HDREZKA.Core.Api;

namespace HDREZKA.App.Services;

/// <summary>Donor perk gate (checksum-validated code, not DRM).</summary>
public static class DonorUnlock
{
    public static bool IsUnlocked => DonorCode.Validate(SettingsService.Instance.DonorCode);
}
