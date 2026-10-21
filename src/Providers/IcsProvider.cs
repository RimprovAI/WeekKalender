using System;
using System.IO;
using System.Net;
using System.Text;
using WeekKalender.Core;

namespace WeekKalender.Providers
{
    /// <summary>
    /// Leest een .ics-bestand of een gepubliceerde agenda-link (alleen als de gebruiker die zelf heeft ingesteld).
    /// Een link wordt hooguit eens per cacheperiode opgehaald; een bestand alleen opnieuw als het is gewijzigd.
    /// </summary>
    public sealed class IcsProvider : IAppointmentProvider
    {
        readonly string _source;
        readonly TimeSpan _urlLifetime;
        readonly Func<DateTime> _now;
        readonly TimeZoneInfo _zone;
        IcsCalendar _calendar;
        DateTime _loadedAt;
        DateTime _fileStamp;

        public IcsProvider(string source, TimeSpan urlLifetime, Func<DateTime> now = null, TimeZoneInfo zone = null)
        {
            _source = Environment.ExpandEnvironmentVariables((source ?? "").Trim().Trim('"'));
            _urlLifetime = urlLifetime;
            _now = now ?? (() => DateTime.Now);
            _zone = zone;
        }

        public string Name
        {
            get { return "ICS"; }
        }

        bool IsUrl
        {
            get
            {
                return _source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       _source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                       _source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ||
                       _source.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase);
            }
        }

        public ProviderResult Fetch(DateTime start, DateTime end)
        {
            try
            {
                IcsCalendar calendar = Load();
                return ProviderResult.Success(calendar.Expand(start, end, _zone ?? TimeZoneInfo.Local), Name);
            }
            catch (WebException ex)
            {
                Log.Warn("ICS-link ophalen: " + ex.Message);
                return ProviderResult.NotAvailable("De agenda-link kon niet worden opgehaald: " + ex.Message);
            }
            catch (FileNotFoundException)
            {
                return ProviderResult.NotAvailable("Het .ics-bestand is niet gevonden: " + _source);
            }
            catch (DirectoryNotFoundException)
            {
                return ProviderResult.NotAvailable("Het .ics-bestand is niet gevonden: " + _source);
            }
            catch (IOException ex)
            {
                return ProviderResult.NotAvailable("Het .ics-bestand kon niet worden gelezen: " + ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return ProviderResult.NotAvailable("Geen toegang tot het .ics-bestand: " + _source);
            }
            catch (Exception ex)
            {
                Log.Warn("ICS lezen: " + ex.Message);
                return ProviderResult.Failed("Het .ics-bestand kon niet worden gelezen: " + ex.Message);
            }
        }

        IcsCalendar Load()
        {
            if (IsUrl)
            {
                if (_calendar != null && _now() - _loadedAt < _urlLifetime) return _calendar;
                _calendar = IcsCalendar.Parse(Download());
                _loadedAt = _now();
                return _calendar;
            }

            DateTime stamp = File.GetLastWriteTimeUtc(_source);
            if (!File.Exists(_source)) throw new FileNotFoundException(_source);
            if (_calendar != null && stamp == _fileStamp) return _calendar;
            _calendar = IcsCalendar.Parse(File.ReadAllText(_source, Encoding.UTF8));
            _fileStamp = stamp;
            return _calendar;
        }

        string Download()
        {
            string url = _source;
            if (url.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(10);
            else if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.UserAgent = "WeekKalender";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (request.Proxy != null) request.Proxy.Credentials = CredentialCache.DefaultCredentials;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        public void Dispose()
        {
        }
    }
}
