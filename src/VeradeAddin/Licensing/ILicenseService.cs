namespace VeradeAddin.Licensing
{
    public enum LicenseStatus
    {
        /// <summary>Valid license key activated: full use.</summary>
        Licensed,
        /// <summary>Inside the free-trial window.</summary>
        Trial,
        /// <summary>Trial window over and no license key.</summary>
        TrialExpired,
        /// <summary>A license was activated but its expiry date has passed: needs renewal.</summary>
        LicenseExpired,
        /// <summary>Stored trial data was edited or the system clock was rolled back.</summary>
        Tampered
    }

    /// <summary>
    /// Offline licensing: a fixed-length free trial persisted (HMAC-signed) in two independent
    /// stores, unlockable with an RSA-signed license key. No server involved.
    /// </summary>
    public interface ILicenseService
    {
        LicenseStatus Status { get; }

        /// <summary>Whole days of trial remaining (0 when expired or licensed).</summary>
        int TrialDaysLeft { get; }

        /// <summary>Customer name embedded in the activated key, or null.</summary>
        string LicensedTo { get; }

        /// <summary>License expiry (UTC), or null when perpetual / not licensed.</summary>
        System.DateTime? ExpiresUtc { get; }

        /// <summary>True when commands may run (licensed or trial active).</summary>
        bool CanRun { get; }

        /// <summary>
        /// Validates and stores a license key. On failure returns false with a
        /// user-presentable (Spanish) reason in <paramref name="error"/>.
        /// </summary>
        bool TryActivate(string key, out string error);
    }
}
