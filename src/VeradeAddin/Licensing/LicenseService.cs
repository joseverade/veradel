using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VeradeAddin.Licensing
{
    /// <summary>
    /// Offline trial + license-key engine. Design constraints: zero server cost, survive an
    /// uninstall/reinstall (that must NOT reset the trial), and never throw into SolidWorks.
    ///
    /// Trial state lives in TWO stores at once — HKCU\Software\Veradel and
    /// %ProgramData%\Veradel\veradel.dat — each carrying the same HMAC-signed record. On load the
    /// valid stores are merged (earliest first-run wins, latest last-run wins), so deleting one
    /// copy achieves nothing. A record that fails its HMAC counts as tampering, not as a fresh
    /// trial. Last-run also moves only forward: a clock rolled back more than a day blocks the
    /// add-in until the clock is fixed.
    ///
    /// License keys are "base64(payload).base64(RSA-SHA256 signature)" where payload is
    /// "VRDL1|name|email|expiryUtcTicks" (0 = perpetual), signed offline with the private key in
    /// %USERPROFILE%\VeradelLicensing (see tools\licensing). Only the public key ships here.
    /// </summary>
    public sealed class LicenseService : ILicenseService
    {
        private const int TrialDays = 30;
        private const string RegistryPath = @"Software\Veradel";
        private const string RegistryValue = "State";
        private const string StateVersion = "1";
        private const string KeyPayloadPrefix = "VRDL1";

        // Public half of the signing keypair (private half never ships).
        private const string PublicKeyXml =
            "<RSAKeyValue><Modulus>vYJFWyL9nPIXDY9NleyM4f1BOoFYUcmyV9zhd+uu/Bn0WSARyJvesTeDn9m8Oz66" +
            "5cE/qP/+s0UdPdDG5SNNwQPY/oBo9v7cEIutezzDkMntlApLMwEESty848W6HjE3Ew39KoT9eYFnKfy0mZMR" +
            "LkWzHsEk9q69wPw/dP+LcvrMNy/ZQGRievclqiSvtM/e3jWbt01qh0d8DQTsD7c3ZKbcv+u5kRx6vuouGnOl" +
            "x7gv5YOYqJRy2th2eFBbxzfKRwExaq/wETt20vx6Zwn2RYfa2SX5W7GQE9P6VBUJ034m1ybEUzTz7ktaHpzl" +
            "nJ2xdfG22xkei5H/jmmwa2QRIQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // Keyed-hash secret for the trial record. Stops a casual editor from forging the record;
        // a determined decompiler is out of scope for this protection level.
        private static readonly byte[] HmacSecret =
        {
            0x9D, 0x8A, 0x4C, 0x96, 0x2A, 0xDD, 0xAA, 0x49, 0xFC, 0x36, 0x26, 0xA7, 0x7D, 0x17, 0xFA, 0xD0,
            0x2D, 0x60, 0xC8, 0x38, 0x3B, 0x79, 0x3D, 0xED, 0xB5, 0xD1, 0x85, 0x80, 0xF3, 0x40, 0x78, 0x6B
        };

        private DateTime _firstRunUtc;
        private DateTime _lastRunUtc;
        private string _licenseKey = "";

        private enum KeyCheck { Valid, Expired, Invalid }

        public LicenseStatus Status { get; private set; }
        public int TrialDaysLeft { get; private set; }
        public string LicensedTo { get; private set; }
        public DateTime? ExpiresUtc { get; private set; }

        public bool CanRun
        {
            get { return Status == LicenseStatus.Licensed || Status == LicenseStatus.Trial; }
        }

        public LicenseService()
        {
            try
            {
                Load();
            }
            catch
            {
                // Any unexpected failure degrades to "trial expired" rather than crashing
                // SolidWorks or silently granting a fresh trial.
                Status = LicenseStatus.TrialExpired;
                TrialDaysLeft = 0;
            }
        }

        public bool TryActivate(string key, out string error)
        {
            string name;
            DateTime? expiresUtc;
            if (VerifyKey(key, out name, out expiresUtc, out error) != KeyCheck.Valid)
            {
                return false;
            }

            _licenseKey = Normalize(key);
            LicensedTo = name;
            ExpiresUtc = expiresUtc;
            Status = LicenseStatus.Licensed;
            TrialDaysLeft = 0;
            Save();
            return true;
        }

        // ---- state machine ---------------------------------------------------------------

        private void Load()
        {
            var now = DateTime.UtcNow;

            DateTime regFirst, regLast, fileFirst, fileLast;
            string regKey, fileKey;
            bool regPresent, filePresent;
            bool regValid = TryParse(ReadRegistry(), out regFirst, out regLast, out regKey, out regPresent);
            bool fileValid = TryParse(ReadFile(), out fileFirst, out fileLast, out fileKey, out filePresent);

            if (!regValid && !fileValid && (regPresent || filePresent))
            {
                // Records exist but none passes its HMAC: someone edited them. Do not restart
                // the trial from an edited record.
                Status = LicenseStatus.Tampered;
                TrialDaysLeft = 0;
                return;
            }

            if (regValid && fileValid)
            {
                _firstRunUtc = regFirst < fileFirst ? regFirst : fileFirst;
                _lastRunUtc = regLast > fileLast ? regLast : fileLast;
                _licenseKey = regKey.Length > 0 ? regKey : fileKey;
            }
            else if (regValid)
            {
                _firstRunUtc = regFirst; _lastRunUtc = regLast; _licenseKey = regKey;
            }
            else if (fileValid)
            {
                _firstRunUtc = fileFirst; _lastRunUtc = fileLast; _licenseKey = fileKey;
            }
            else
            {
                // Genuine first run.
                _firstRunUtc = now;
                _lastRunUtc = now;
                _licenseKey = "";
            }

            // Clock rollback: last-run may only move forward (26 h of slack covers timezone
            // and DST games without punishing legitimate clock fixes).
            if (now < _lastRunUtc.AddHours(-26))
            {
                Status = LicenseStatus.Tampered;
                TrialDaysLeft = 0;
                Save(); // keep the (higher) last-run so re-running doesn't clear the block
                return;
            }

            if (now > _lastRunUtc)
            {
                _lastRunUtc = now;
            }

            string name, error;
            DateTime? expiresUtc;
            KeyCheck check = _licenseKey.Length > 0
                ? VerifyKey(_licenseKey, out name, out expiresUtc, out error)
                : Invalid(out name, out expiresUtc, out error);

            if (check == KeyCheck.Valid)
            {
                Status = LicenseStatus.Licensed;
                LicensedTo = name;
                ExpiresUtc = expiresUtc;
                TrialDaysLeft = 0;
            }
            else if (check == KeyCheck.Expired)
            {
                // An annual license ran out: ask for a renewal, don't fall back to the
                // (long-gone) trial messaging.
                Status = LicenseStatus.LicenseExpired;
                LicensedTo = name;
                ExpiresUtc = expiresUtc;
                TrialDaysLeft = 0;
            }
            else
            {
                int daysUsed = (int)(now - _firstRunUtc).TotalDays;
                TrialDaysLeft = TrialDays - daysUsed;
                if (TrialDaysLeft < 0) TrialDaysLeft = 0;
                Status = TrialDaysLeft > 0 ? LicenseStatus.Trial : LicenseStatus.TrialExpired;
            }

            Save();
        }

        // ---- license key verification ----------------------------------------------------

        private static KeyCheck Invalid(out string licensedTo, out DateTime? expiresUtc, out string error)
        {
            licensedTo = null;
            expiresUtc = null;
            error = null;
            return KeyCheck.Invalid;
        }

        private static KeyCheck VerifyKey(string key, out string licensedTo, out DateTime? expiresUtc,
            out string error)
        {
            licensedTo = null;
            expiresUtc = null;
            error = null;
            try
            {
                key = Normalize(key);
                var parts = key.Split('.');
                if (parts.Length != 2)
                {
                    error = "El formato de la clave no es válido.";
                    return KeyCheck.Invalid;
                }

                var payload = Convert.FromBase64String(parts[0]);
                var signature = Convert.FromBase64String(parts[1]);

                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    if (!rsa.VerifyData(payload, "SHA256", signature))
                    {
                        error = "La clave no es válida.";
                        return KeyCheck.Invalid;
                    }
                }

                // "VRDL1|name|email|expiryUtcTicks"
                var fields = Encoding.UTF8.GetString(payload).Split('|');
                if (fields.Length != 4 || fields[0] != KeyPayloadPrefix)
                {
                    error = "La clave no es válida.";
                    return KeyCheck.Invalid;
                }

                licensedTo = fields[1];

                long expiryTicks = long.Parse(fields[3], CultureInfo.InvariantCulture);
                if (expiryTicks != 0)
                {
                    expiresUtc = new DateTime(expiryTicks, DateTimeKind.Utc);
                    if (DateTime.UtcNow.Ticks > expiryTicks)
                    {
                        error = "La licencia caducó el " +
                            expiresUtc.Value.ToLocalTime().ToString("dd/MM/yyyy") + ".";
                        return KeyCheck.Expired;
                    }
                }

                return KeyCheck.Valid;
            }
            catch
            {
                error = "El formato de la clave no es válido.";
                return KeyCheck.Invalid;
            }
        }

        private static string Normalize(string key)
        {
            if (key == null) return "";
            var sb = new StringBuilder(key.Length);
            foreach (var c in key)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- signed record serialization -------------------------------------------------

        private string Serialize()
        {
            string payload = StateVersion + "|" +
                _firstRunUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                _lastRunUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                _licenseKey;
            return payload + "|" + ComputeHmac(payload);
        }

        private static bool TryParse(string text, out DateTime firstUtc, out DateTime lastUtc,
            out string licenseKey, out bool present)
        {
            firstUtc = DateTime.MinValue;
            lastUtc = DateTime.MinValue;
            licenseKey = "";
            present = !string.IsNullOrEmpty(text);
            if (!present) return false;

            try
            {
                // "version|first|last|key|hmac" — the key never contains '|' (base64.base64).
                var parts = text.Split('|');
                if (parts.Length != 5 || parts[0] != StateVersion) return false;

                string payload = parts[0] + "|" + parts[1] + "|" + parts[2] + "|" + parts[3];
                if (!string.Equals(ComputeHmac(payload), parts[4], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                firstUtc = new DateTime(long.Parse(parts[1], CultureInfo.InvariantCulture), DateTimeKind.Utc);
                lastUtc = new DateTime(long.Parse(parts[2], CultureInfo.InvariantCulture), DateTimeKind.Utc);
                licenseKey = parts[3];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeHmac(string payload)
        {
            using (var hmac = new HMACSHA256(HmacSecret))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---- the two stores --------------------------------------------------------------

        private void Save()
        {
            string record = Serialize();
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null) key.SetValue(RegistryValue, record);
                }
            }
            catch { }

            try
            {
                string path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, record);
            }
            catch { }
        }

        private static string ReadRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    return key == null ? null : key.GetValue(RegistryValue) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ReadFile()
        {
            try
            {
                string path = FilePath();
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string FilePath()
        {
            // ProgramData survives per-user cleanup and reinstalls; the installer never touches it.
            string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(root, "Veradel", "veradel.dat");
        }
    }
}
