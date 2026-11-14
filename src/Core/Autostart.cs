using System;
using System.IO;
using Microsoft.Win32;

namespace WeekKalender.Core
{
    /// <summary>
    /// Autostart via een snelkoppeling in de map Opstarten van de gebruiker (geen adminrechten nodig).
    /// Houdt rekening met de aan/uit-schakelaar die Taakbeheer onder StartupApproved\StartupFolder bijhoudt.
    /// Het programma schrijft nergens anders in het register.
    /// </summary>
    public sealed class Autostart
    {
        public const string DefaultApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        static readonly byte[] EnabledFlag = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        readonly string _exePath;
        readonly string _name;
        readonly string _startupFolder;
        readonly string _approvedFolderKey;

        public Autostart(string exePath, string name = "WeekKalender", string startupFolder = null,
                         string approvedFolderKey = DefaultApprovedFolderKey)
        {
            _exePath = exePath;
            _name = name;
            _startupFolder = startupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
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
                if (approved == null) return;
                byte[] flag = approved.GetValue(ShortcutName) as byte[];
                if (flag != null && flag.Length > 0 && (flag[0] & 1) == 1)
                    approved.SetValue(ShortcutName, EnabledFlag, RegistryValueKind.Binary);
            }
        }

        public void Disable()
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedFolderKey, true))
            {
                if (approved != null) approved.DeleteValue(ShortcutName, false);
            }
        }

        internal static bool SamePath(string path, string exePath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))),
                                     Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
