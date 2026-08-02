using System;
using System.Diagnostics;
using System.IO;

namespace PWFAuth
{
    /// <summary>
    /// Produces the stable per-machine identifier a license binds to.
    /// </summary>
    /// <remarks>
    /// Deliberately free of extra package dependencies and of <c>wmic</c>, which is
    /// deprecated and has been removed from Windows 11 24H2 — anything built on it
    /// silently degrades to a weak fallback on current machines. Windows reads the
    /// cryptography MachineGuid, Linux reads /etc/machine-id, macOS reads the platform
    /// UUID. Supply your own via <see cref="PwfClientOptions.HardwareId"/> if you need
    /// a different binding policy.
    /// </remarks>
    public static class HardwareId
    {
        private static string? _cached;

        /// <summary>
        /// Returns the machine identifier, computed once per process. Never throws and
        /// never returns null — it degrades to the machine name if nothing better exists.
        /// </summary>
        public static string Get()
        {
            if (_cached != null) return _cached;
            string value = Resolve();
            if (string.IsNullOrWhiteSpace(value)) value = SafeMachineName();
            _cached = value.Trim();
            return _cached;
        }

        private static string Resolve()
        {
            try
            {
#if NET8_0_OR_GREATER
                if (OperatingSystem.IsWindows()) return WindowsMachineGuid();
                if (OperatingSystem.IsLinux()) return ReadFile("/etc/machine-id");
                if (OperatingSystem.IsMacOS()) return MacPlatformUuid();
#else
                PlatformID platform = Environment.OSVersion.Platform;
                if (platform == PlatformID.Win32NT) return WindowsMachineGuid();
                if (File.Exists("/etc/machine-id")) return ReadFile("/etc/machine-id");
                if (Directory.Exists("/System/Library")) return MacPlatformUuid();
#endif
            }
            catch (Exception)
            {
                // Any probe failure falls through to the machine-name fallback.
            }
            return string.Empty;
        }

        // reg.exe rather than the Registry API so the package stays dependency-free on
        // netstandard2.0 (where Microsoft.Win32.Registry is a separate package).
        private static string WindowsMachineGuid()
        {
            string output = RunCommand("reg",
                @"query HKLM\SOFTWARE\Microsoft\Cryptography /v MachineGuid");
            if (string.IsNullOrEmpty(output)) return string.Empty;

            // "    MachineGuid    REG_SZ    2f5a1c8e-...."
            int marker = output.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return string.Empty;
            return output.Substring(marker + "REG_SZ".Length).Trim();
        }

        private static string MacPlatformUuid()
        {
            string output = RunCommand("ioreg", "-rd1 -c IOPlatformExpertDevice");
            const string key = "IOPlatformUUID";
            int at = output.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return string.Empty;
            int open = output.IndexOf('"', at + key.Length);
            if (open < 0) return string.Empty;
            open = output.IndexOf('"', open + 1);
            if (open < 0) return string.Empty;
            int close = output.IndexOf('"', open + 1);
            return close > open ? output.Substring(open + 1, close - open - 1) : string.Empty;
        }

        private static string ReadFile(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        private static string RunCommand(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,   // no console flash in a WinForms/WPF app
                };
                using (Process? proc = Process.Start(psi))
                {
                    if (proc == null) return string.Empty;
                    string output = proc.StandardOutput.ReadToEnd();
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        return string.Empty;
                    }
                    return output;
                }
            }
            catch (Exception) { return string.Empty; }
        }

        private static string SafeMachineName()
        {
            try { return Environment.MachineName; }
            catch (Exception) { return "unknown-machine"; }
        }
    }
}
