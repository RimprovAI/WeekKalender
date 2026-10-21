using System;
using System.IO;
using Microsoft.Win32;

namespace WeekKalender.Core
{
    /// <summary>
    /// Autostart via HKCU\...\Run (geen adminrechten nodig). Houdt ook rekening met de aan/uit-schakelaar
    /// die Taakbeheer onder StartupApproved bijhoudt.
    /// </summary>
    public sealed class Autostart
    {
        public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static readonly byte[] EnabledFlag = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        readonly string _exePath;
        readonly string _name;
        readonly string _runKey;
        readonly string _approvedKey;

        public Autostart(string exePath, string name = "WeekKalender", string runKey = DefaultRunKey, string approvedKey = DefaultApprovedKey)
        {
            _exePath = exePath;
            _name = name;
            _runKey = runKey;
            _approvedKey = approvedKey;
        }

        public string Command
        {
            get { return "\"" + _exePath + "\""; }
        }

        /// <summary>Aan als de Run-waarde naar dit programma wijst en Taakbeheer hem niet heeft uitgeschakeld.</summary>
        public bool IsEnabled()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(_runKey))
            {
                string value = run == null ? null : run.GetValue(_name) as string;
                if (value == null || !PointsTo(value, _exePath)) return false;
            }
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedKey))
            {
                byte[] flag = approved == null ? null : approved.GetValue(_name) as byte[];
                if (flag != null && flag.Length > 0 && (flag[0] & 1) == 1) return false;
            }
            return true;
        }

        public void Enable()
        {
            using (var run = Registry.CurrentUser.CreateSubKey(_runKey))
            {
                run.SetValue(_name, Command, RegistryValueKind.String);
            }
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedKey, true))
            {
                if (approved == null) return;
                byte[] flag = approved.GetValue(_name) as byte[];
                if (flag != null && flag.Length > 0 && (flag[0] & 1) == 1)
                    approved.SetValue(_name, EnabledFlag, RegistryValueKind.Binary);
            }
        }

        public void Disable()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(_runKey, true))
            {
                if (run != null) run.DeleteValue(_name, false);
            }
            using (var approved = Registry.CurrentUser.OpenSubKey(_approvedKey, true))
            {
                if (approved != null) approved.DeleteValue(_name, false);
            }
        }

        internal static bool PointsTo(string command, string exePath)
        {
            string path = command.Trim();
            if (path.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = path.IndexOf('"', 1);
                if (end > 0) path = path.Substring(1, end - 1);
            }
            try
            {
                return string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
