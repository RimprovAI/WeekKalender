using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("Michalendar (WeekKalender)")]
[assembly: AssemblyDescription("Weeknummer in het systeemvak met agenda; leest Outlook-afspraken alleen-lezen")]
[assembly: AssemblyProduct("WeekKalender")]
[assembly: AssemblyCopyright("Intern hulpmiddel, broncode meegeleverd")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]
[assembly: AssemblyInformationalVersion("1.5.0")]
[assembly: ComVisible(false)]

// Systeembibliotheken (user32, shell32, dwmapi, shcore, ole32) worden alleen uit System32 geladen.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Zonder dit attribuut gedraagt .NET Framework zich als versie 4.0 (o.a. geen DPI-schaling per monitor in WPF).
[assembly: TargetFramework(".NETFramework,Version=v4.8.1", FrameworkDisplayName = ".NET Framework 4.8.1")]
