using System;
using System.Collections.Generic;

namespace GestionHoraire.Models.ViewModels
{
    public class ManageTwoFactorViewModel
    {
        public bool IsTwoFactorEnabled { get; set; }
        public bool IsAuthenticatorConfigured { get; set; }
        public bool IsSetupInProgress { get; set; }
        public string ProviderLabel { get; set; } = "Aucune";
        public string? ManualEntryKey { get; set; }
        public string? QrCodeSvg { get; set; }
        public string? AccountLabel { get; set; }
        public int RemainingBackupCodes { get; set; }
        public IReadOnlyList<string> RecoveryCodes { get; set; } = Array.Empty<string>();
        public IReadOnlyList<TrustedDeviceViewModel> TrustedDevices { get; set; } = Array.Empty<TrustedDeviceViewModel>();
    }

    public class TrustedDeviceViewModel
    {
        public int Id { get; set; }
        public string DeviceName { get; set; } = "Appareil Web";
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsCurrentDevice { get; set; }
    }
}
