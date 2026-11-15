# Beveiligingsoverzicht voor IT-beoordeling

Dit document beschrijft wat `WeekKalender.exe` (schermnaam Michalendar) doet en niet doet, zodat een IT-afdeling het kan beoordelen. Alles hieronder is na te lopen in de meegeleverde broncode (ongeveer 4.500 regels C#, geen externe bibliotheken).

## Samenvatting

| Onderwerp | Feit |
|---|---|
| Soort programma | Tray-icoon met weeknummer en een agenda-popup |
| Rechten | Draait als gewone gebruiker (`asInvoker`); vraagt nooit om verhoging |
| Installatie | Geen. Eén exe, geen service, geen driver, geen geplande taak |
| Afhankelijkheden | Alleen .NET Framework 4.8 (onderdeel van Windows). Geen NuGet-pakketten, geen meegeleverde DLL's |
| Netwerk | Standaard **geen enkele** verbinding. Alleen als de gebruiker zelf een agenda-link instelt: één HTTPS-GET naar dat adres |
| Telemetrie, updates | Geen. Het programma belt nergens naartoe en werkt zichzelf niet bij |
| Outlook | Alleen lezen via het lokale Outlook-objectmodel; schrijven is in de code onmogelijk gemaakt |
| Gegevens op schijf | `%APPDATA%\WeekKalender\instellingen.ini` en `log.txt` (zonder inhoud van afspraken) |
| Register | Leest één themawaarde. Schrijft alleen de aan/uit-vlag van zijn eigen opstartsnelkoppeling (HKCU) |
| Ondertekening | **Niet digitaal ondertekend** (zie "Bekende aandachtspunten") |

## Wat het programma aanraakt

**Bestanden**
- Leest en schrijft: `%APPDATA%\WeekKalender\instellingen.ini`, `log.txt` (maximaal 256 KB, daarna één `.old`).
- Alleen als de gebruiker *Autostart* aanzet: `WeekKalender.lnk` in de map Opstarten van de gebruiker.
- Alleen als de gebruiker het instelt: leest één `.ics`-bestand (maximaal 10 MB).

**Register (alleen HKEY_CURRENT_USER)**
- Leest `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`.
- Leest/schrijft de eigen waarde `WeekKalender.lnk` onder `...\Explorer\StartupApproved\StartupFolder` (de schakelaar die Taakbeheer ook gebruikt), alleen bij het aan- of uitzetten van autostart.
- Geen Run-sleutels, niets onder HKLM.

**Andere processen**
- Klassiek Outlook via COM (`Outlook.Application`). Draait Outlook niet, dan wordt het onzichtbaar gestart en na 5 minuten zonder gebruik weer losgelaten. Dit is uit te zetten met `OutlookStarten=nee`, of helemaal met `Bron=geen`.
- Verkenner (`Shell_NotifyIcon`) voor het tray-icoon. Geen code-injectie, geen hooks, geen toetsenbord- of schermopname.

**Netwerk**
- Geen, tenzij `IcsBron` een link bevat. Dan: alleen `https://` (http wordt geweigerd, `webcal://` wordt https), certificaatcontrole door Windows, geen aanmeldgegevens naar de server, doorverwijzing naar onversleuteld adres geweigerd, antwoord maximaal 10 MB, hooguit eens per 5 minuten.
- Het adres van de link wordt niet gelogd.

## Maatregelen in de code

- **Outlook alleen-lezen, afgedwongen.** Elke Outlook-aanroep loopt via `src\Providers\OutlookCom.cs`. Die laat alleen een vaste lijst namen door: tien leeseigenschappen (o.a. `Subject`, `Start`, `End`, `Location`), tien zoek-/toonmethodes (o.a. `Restrict`, `GetNext`, `Display`) en één instelling op de zoeklijst (`IncludeRecurrences`). `Save`, `Delete`, `Send`, `Move`, `Close` en het wijzigen van eigenschappen worden geweigerd voordat Outlook ze ziet. `build.ps1` breekt de build af als ergens `dynamic` of een directe `InvokeMember` buiten dit bestand staat. Er is een test die de schrijvende namen probeert.
- **Geen eigenschappen die de Outlook-beveiligingsmelding oproepen** (geen adressen, deelnemers of berichttekst).
- **DLL-zoekpad.** Eigen systeemaanroepen laden alleen uit `System32` (`DefaultDllImportSearchPaths`), en de werkmap is uit het zoekpad gehaald (`SetDllDirectory("")`).
- **Geen ongedocumenteerde Windows-functies.**
- **Invoer van buiten wordt begrensd.** `.ics`-inhoud wordt alleen als tekst ontleed, nooit uitgevoerd; limieten op bestandsgrootte, aantal afspraken en herhalingen; fouten in één regel breken de rest niet.
- **Niets blokkeert of crasht op Outlook.** Outlook-aanroepen lopen op een eigen thread met time-out.
- **Compiler:** waarschuwingen zijn fouten (`/warnaserror`), build is deterministisch.

## Zelf controleren

1. **Broncode lezen.** Begin bij `OutlookCom.cs`, `IcsProvider.cs`, `Autostart.cs` en `NativeMethods.cs` (alle Windows-aanroepen staan daar).
2. **Zelf bouwen en vergelijken.** `powershell -ExecutionPolicy Bypass -File build.ps1` bouwt de exe met de C#-compiler van Visual Studio Build Tools en toont de SHA-256. De build is deterministisch: dezelfde broncode en compilerversie geven byte voor byte dezelfde exe. De hash van de meegeleverde exe staat in `dist\SHA256.txt`.
3. **Tests draaien.** Dezelfde opdracht draait 59 tests, waaronder die voor de alleen-lezen-lijst, de https-eis en de groottelimiet.
4. **Gedrag meten.** Met Process Monitor en een netwerkmonitor is te zien dat het programma in rust niets leest, schrijft of verstuurt.

## Bekende aandachtspunten

Deze punten kan de maker niet zelf oplossen; ze horen bij de beoordeling.

- **Geen digitale handtekening.** SmartScreen kan bij een gedownloade kopie "onbekende app" melden, en AppLocker/WDAC-beleid dat alleen ondertekende software toestaat, blokkeert het programma. Oplossing: laten ondertekenen met het code-signing-certificaat van de organisatie, of op hash vrijgeven (zie `dist\SHA256.txt`).
- **Gedrag dat beveiligingssoftware opvalt.** Het programma zet (op verzoek) een opstartsnelkoppeling en leest Outlook via COM. Dat is hier onschuldig en aantoonbaar alleen-lezen, maar het zijn patronen waar EDR-producten op letten. Een beoordeling vooraf voorkomt een melding achteraf.
- **Niet gescand door een externe dienst.** De exe is niet naar VirusTotal of een vergelijkbare dienst gestuurd. Op de ontwikkel-pc (Trend Micro met Defender passief) is er geen detectie geweest.
- **Start klassiek Outlook op de achtergrond** als de gebruiker nieuwe Outlook gebruikt. Wie dat niet wil, zet `OutlookStarten=nee`.
- **Gepubliceerde agenda-link.** Wie een `.ics`-link gebruikt, moet weten dat iedereen met die link de agenda kan lezen. Dat is een eigenschap van de link, niet van dit programma.
- **Geen onafhankelijke audit of pentest.** De code is nagelopen door de maker en door AI-assistenten, niet door een beveiligingsbedrijf.
