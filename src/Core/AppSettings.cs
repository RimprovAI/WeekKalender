using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeekKalender.Core
{
    /// <summary>
    /// Instellingen in %APPDATA%\WeekKalender\instellingen.ini. Wijzigingen worden opgepakt bij het volgende openen van de popup.
    /// </summary>
    public sealed class AppSettings
    {
        public const string SourceAuto = "auto";
        public const string SourceOutlook = "outlook";
        public const string SourceIcs = "ics";
        public const string SourceNone = "geen";

        DateTime _stamp;

        public AppSettings()
        {
            Source = SourceAuto;
            IcsSource = "";
            StartOutlook = true;
            CacheMinutes = 5;
        }

        /// <summary>auto, outlook, ics of geen.</summary>
        public string Source { get; set; }

        /// <summary>Pad naar een .ics-bestand of een http(s)/webcal-link.</summary>
        public string IcsSource { get; set; }

        /// <summary>Klassiek Outlook onzichtbaar starten als het niet draait.</summary>
        public bool StartOutlook { get; set; }

        public int CacheMinutes { get; set; }

        public string FilePath { get; private set; }

        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeekKalender"); }
        }

        public static AppSettings Load(string path)
        {
            var settings = new AppSettings { FilePath = path };
            try
            {
                if (!File.Exists(path))
                {
                    settings.Save();
                }
                else
                {
                    settings.Read(File.ReadAllLines(path, Encoding.UTF8));
                }
                settings._stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                Log.Warn("Instellingen konden niet worden gelezen: " + ex.Message);
            }
            return settings;
        }

        /// <summary>Leest het bestand opnieuw als het sinds de vorige keer is gewijzigd.</summary>
        public bool ReloadIfChanged()
        {
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) return false;
                DateTime stamp = File.GetLastWriteTimeUtc(FilePath);
                if (stamp == _stamp) return false;
                var fresh = new AppSettings();
                fresh.Read(File.ReadAllLines(FilePath, Encoding.UTF8));
                Source = fresh.Source;
                IcsSource = fresh.IcsSource;
                StartOutlook = fresh.StartOutlook;
                CacheMinutes = fresh.CacheMinutes;
                _stamp = stamp;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Instellingen konden niet opnieuw worden gelezen: " + ex.Message);
                return false;
            }
        }

        internal void Read(IEnumerable<string> lines)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "bron":
                        Source = NormalizeSource(value);
                        break;
                    case "icsbron":
                        IcsSource = value.Trim('"');
                        break;
                    case "outlookstarten":
                        bool start;
                        if (TryParseBool(value, out start)) StartOutlook = start;
                        break;
                    case "cacheminuten":
                        int minutes;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                            CacheMinutes = Math.Max(1, Math.Min(120, minutes));
                        break;
                }
            }
        }

        static string NormalizeSource(string value)
        {
            string v = value.Trim().ToLowerInvariant();
            if (v == SourceAuto || v == SourceOutlook || v == SourceIcs || v == SourceNone) return v;
            Log.Warn("Onbekende waarde voor Bron: '" + value + "', 'auto' wordt gebruikt.");
            return SourceAuto;
        }

        static bool TryParseBool(string value, out bool result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "ja": case "aan": case "true": case "yes": case "1":
                    result = true;
                    return true;
                case "nee": case "uit": case "false": case "no": case "0":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        public void Save()
        {
            if (FilePath == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var sb = new StringBuilder();
            sb.AppendLine("; WeekKalender - instellingen. Wijzigingen gelden vanaf de volgende keer dat je de agenda opent.");
            sb.AppendLine(";");
            sb.AppendLine("; Bron: waar de afspraken vandaan komen");
            sb.AppendLine(";   auto    = klassiek Outlook; lukt dat niet en is IcsBron ingevuld, dan het .ics-bestand");
            sb.AppendLine(";   outlook = alleen klassiek Outlook");
            sb.AppendLine(";   ics     = alleen IcsBron");
            sb.AppendLine(";   geen    = alleen weeknummers, geen afspraken");
            sb.AppendLine("Bron=" + Source);
            sb.AppendLine();
            sb.AppendLine("; IcsBron: pad naar een .ics-bestand of een link naar een gepubliceerde agenda (https:// of webcal://)");
            sb.AppendLine("IcsBron=" + IcsSource);
            sb.AppendLine();
            sb.AppendLine("; OutlookStarten: ja = klassiek Outlook onzichtbaar starten als het niet draait, nee = alleen lezen als het al open is");
            sb.AppendLine("OutlookStarten=" + (StartOutlook ? "ja" : "nee"));
            sb.AppendLine();
            sb.AppendLine("; CacheMinuten: hoe lang opgehaalde afspraken worden bewaard voordat ze opnieuw worden gelezen (1-120)");
            sb.AppendLine("CacheMinuten=" + CacheMinutes.ToString(CultureInfo.InvariantCulture));

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
            _stamp = File.GetLastWriteTimeUtc(FilePath);
        }
    }
}
