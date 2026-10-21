using System;
using System.IO;
using System.Text;

namespace WeekKalender.Core
{
    /// <summary>Klein logbestand in %APPDATA%\WeekKalender. Bevat nooit inhoud van afspraken.</summary>
    public static class Log
    {
        const long MaxBytes = 256 * 1024;
        static readonly object Lock = new object();
        static string _path;

        public static void Init(string path)
        {
            _path = path;
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("LET OP", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("FOUT", ex == null ? message : message + ": " + ex);
        }

        static void Write(string level, string message)
        {
            if (_path == null) return;
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    var info = new FileInfo(_path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        File.Copy(_path, _path + ".old", true);
                        File.Delete(_path);
                    }
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Loggen mag de app nooit laten vallen.
            }
        }
    }
}
