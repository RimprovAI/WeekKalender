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
    ///
    /// Veiligheid: alleen https (geen onversleuteld http), certificaatcontrole van Windows, geen aanmeldgegevens naar
    /// de server, een limiet op de grootte, en de inhoud wordt alleen als tekst ontleed (nooit uitgevoerd).
    /// </summary>
    public sealed class IcsProvider : IAppointmentProvider
    {
        public const int DefaultMaxBytes = 10 * 1024 * 1024;

        readonly string _source;
        readonly TimeSpan _urlLifetime;
        readonly Func<DateTime> _now;
        readonly TimeZoneInfo _zone;
        readonly int _maxBytes;
        IcsCalendar _calendar;
        DateTime _loadedAt;
        DateTime _fileStamp;

        public IcsProvider(string source, TimeSpan urlLifetime, Func<DateTime> now = null, TimeZoneInfo zone = null, int maxBytes = DefaultMaxBytes)
        {
            _source = Environment.ExpandEnvironmentVariables((source ?? "").Trim().Trim('"'));
            _urlLifetime = urlLifetime;
            _now = now ?? (() => DateTime.UtcNow);
            _zone = zone;
            _maxBytes = maxBytes;
        }

        public string Name
        {
            get { return "ICS"; }
        }

        bool IsUrl
        {
            get { return _source.IndexOf("://", StringComparison.Ordinal) > 0 && !(_source.Length > 2 && _source[1] == ':'); }
        }

        public ProviderResult Fetch(DateTime start, DateTime end)
        {
            try
            {
                IcsCalendar calendar = Load();
                return ProviderResult.Success(calendar.Expand(start, end, _zone ?? TimeZoneInfo.Local), Name);
            }
            catch (NotSupportedException ex)
            {
                return ProviderResult.NotAvailable(ex.Message);
            }
            catch (WebException ex)
            {
                // Geen adres in het logboek: een gepubliceerde agenda-link is zelf het 'wachtwoord'.
                Log.Warn("ICS-link ophalen mislukt: " + ex.Status);
                return ProviderResult.NotAvailable("De agenda-link kon niet worden opgehaald (" + ex.Status + ").");
            }
            catch (FileNotFoundException)
            {
                return ProviderResult.NotAvailable("Het .ics-bestand is niet gevonden: " + _source);
            }
            catch (DirectoryNotFoundException)
            {
                return ProviderResult.NotAvailable("Het .ics-bestand is niet gevonden: " + _source);
            }
            catch (InvalidDataException ex)
            {
                return ProviderResult.Failed(ex.Message);
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
                Log.Warn("ICS lezen mislukt: " + ex.GetType().Name);
                return ProviderResult.Failed("De .ics-agenda kon niet worden gelezen: " + ex.Message);
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

            var info = new FileInfo(_source);
            if (!info.Exists) throw new FileNotFoundException(_source);
            if (info.Length > _maxBytes) throw new InvalidDataException("Het .ics-bestand is te groot (meer dan " + (_maxBytes / (1024 * 1024)) + " MB).");
            if (_calendar != null && info.LastWriteTimeUtc == _fileStamp) return _calendar;
            _calendar = IcsCalendar.Parse(File.ReadAllText(_source, Encoding.UTF8));
            _fileStamp = info.LastWriteTimeUtc;
            return _calendar;
        }

        /// <summary>Zet webcal(s):// om naar https:// en weigert alles wat niet versleuteld is.</summary>
        internal static Uri SecureUri(string source)
        {
            string url = source;
            if (url.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(10);
            else if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new NotSupportedException("Alleen beveiligde agenda-links (https:// of webcal://) zijn toegestaan.");
            return uri;
        }

        string Download()
        {
            var request = (HttpWebRequest)WebRequest.Create(SecureUri(_source));
            request.Method = "GET";
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.UserAgent = "WeekKalender";
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;
            request.UseDefaultCredentials = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // Alleen de bedrijfsproxy krijgt (indien nodig) de Windows-aanmelding, de agendaserver nooit.
            if (request.Proxy != null) request.Proxy.Credentials = CredentialCache.DefaultCredentials;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps)
                    throw new NotSupportedException("De agenda-link verwijst door naar een onbeveiligd adres; dat is niet toegestaan.");
                if (response.ContentLength > _maxBytes) throw new InvalidDataException("De agenda is te groot.");
                using (var stream = response.GetResponseStream())
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[16384];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        if (buffer.Length > _maxBytes) throw new InvalidDataException("De agenda is te groot.");
                    }
                    return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
