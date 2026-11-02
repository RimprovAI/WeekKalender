using System;
using System.IO;
using Microsoft.Win32;

namespace WeekKalender.Core
{
    /// <summary>
    /// Autostart via een snelkoppeling in de map Opstarten van de gebruiker (geen adminrechten nodig).
    /// Houdt rekening met de aan/uit-schakelaar die Taakbeheer onder StartupApproved\StartupFolder bijhoudt.
    ///
    /// Eerdere versies gebruikten HKCU\...\Run. Op sommige werkplekken slaat Windows die waarde bij het aanmelden
    /// zonder melding over, terwijl de map Opstarten wel wordt uitgevoerd. MigrateFromRunKey zet een oude waarde om.
    /// </summary>
    public sealed class Autostart
    {
        public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string DefaultApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        static readonly byte[] EnabledFlag = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        readonly string _exePath;
        readonly string _name;
        readonly string _startupFolder;
        readonly string _runKey;
        readonly string _approvedRunKey;
        readonly string _approvedFolderKey;

        public Autostart(string exePath, string name = "WeekKalender", string startupFolder = null,
                         string runKey = DefaultRunKey, string approvedRunKey = DefaultApprovedRunKey,
                         string approvedFolderKey = DefaultApprovedFolderKey)
        {
            _exePath = exePath;
            _name = name;
            _startupFolder = startupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            _runKey = runKey;
            _approvedRunKey = approvedRunKey;
            _approvedFolderKey = approvedFolderKey;
        }

        public string ShortcutPath
        {
            get { return Path.Combine(_startupFolder, ShortcutName); }
        }

        string ShortcutName
        {
            get { return _name + ".lnk"; }
        }

        /// <summary>Aan als de snelkoppeling naar dit programma wijst en Taakbeheer hem niet heeft uitgeschakeld.</summary>
        public bool IsEnabled()
        {
            if (!File.Exists(ShortcutPath)) return false;
            if (!SamePath(ShellLink.ReadTarget(ShortcutPath), _exePath)) return false;
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedFolderKey))
            {
                byte[] flag = approved == null ? null : approved.GetValue(ShortcutName) as byte[];
                if (flag != null && flag.Length > 0 && (flag[0] & 1) == 1) return false;
            }
            return true;
        }

        public void Enable()
        {
            Directory.CreateDirectory(_startupFolder);
            ShellLink.Create(ShortcutPath, _exePath, Path.GetDirectoryName(_exePath), "Weeknummer en agenda in het systeemvak");
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedFolderKey, true))
            {
                if (approved != null)
                {
                    byte[] flag = approved.GetValue(ShortcutName) as byte[];
                    if (flag != null && flag.Length > 0 && (flag[0] & 1) == 1)
                        approved.SetValue(ShortcutName, EnabledFlag, RegistryValueKind.Binary);
                }
            }
            RemoveRunValue();
        }

        public void Disable()
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedFolderKey, true))
            {
                if (approved != null) approved.DeleteValue(ShortcutName, false);
            }
            RemoveRunValue();
        }

        /// <summary>
        /// Zet autostart uit een oudere versie (Run-sleutel) om naar de map Opstarten.
        /// Geeft true terug als er iets is omgezet.
        /// </summary>
        public bool MigrateFromRunKey()
        {
            string command;
            using (var run = Registry.CurrentUser.OpenSubKey(_runKey))
            {
                command = run == null ? null : run.GetValue(_name) as string;
            }
            if (command == null) return false;

            bool wasDisabled;
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedRunKey))
            {
                byte[] flag = approved == null ? null : approved.GetValue(_name) as byte[];
                wasDisabled = flag != null && flag.Length > 0 && (flag[0] & 1) == 1;
            }

            // Uitgeschakeld in Taakbeheer: niet stiekem weer aanzetten, alleen de oude waarde opruimen.
            if (wasDisabled) RemoveRunValue();
            else Enable();
            return true;
        }

        void RemoveRunValue()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(_runKey, true))
            {
                if (run != null) run.DeleteValue(_name, false);
            }
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedRunKey, true))
            {
                if (approved != null) approved.DeleteValue(_name, false);
            }
        }

        internal static bool SamePath(string path, string exePath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Trim();
            if (p.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = p.IndexOf('"', 1);
                if (end > 0) p = p.Substring(1, end - 1);
            }
            try
            {
                return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(p)), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
