# WeekKalender

Een klein programma voor het systeemvak van Windows 11. Het icoon bij de klok toont het huidige weeknummer. Klik erop en je krijgt een agenda met weeknummers en je afspraken uit Outlook.

Het programma is één bestand van ongeveer 130 KB (`dist\WeekKalender.exe`). Er is geen installatie nodig, geen adminrechten en geen extra runtime: het draait op .NET Framework 4.8.1, dat standaard in Windows 11 zit.

## Gebruik

Start `WeekKalender.exe`. Het icoon met het weeknummer verschijnt in het systeemvak. Staat het tussen de verborgen pictogrammen (pijltje ^), sleep het dan naar de taakbalk.

- **Linkerklik** op het icoon opent de agenda. Opnieuw klikken, Escape of ergens anders klikken sluit hem.
- **Rechtermuisklik** geeft het menu met *Autostart met Windows* (aan/uit) en *Afsluiten*.
- **De tooltip** toont de volledige datum, bijvoorbeeld "woensdag 21 oktober 2026 – week 43".

In de agenda:

| Wat | Hoe |
|---|---|
| Andere maand | pijltjes rechtsboven, muiswiel boven de kalender, of PageUp/PageDown |
| Dag kiezen | klikken, of de pijltjestoetsen |
| Terug naar vandaag | knop *Vandaag* of Home |
| Naar een week | typ een weeknummer en druk op Enter of *Ga naar week*. Werkt ook direct: popup open, `43` typen, Enter. Met jaar: `43 2027`, `2027-W43` of `w5` |
| Afspraak openen | klik op de afspraak; hij opent in klassiek Outlook |

Zonder jaartal geldt het jaar van de maand die je bekijkt. Bij een maandwissel wordt vandaag geselecteerd als die in die maand valt, anders de 1e.

Weeknummers volgen ISO 8601: de week begint op maandag en week 1 is de week met de eerste donderdag van het jaar. Rond de jaarwisseling kan 1 januari dus in week 53 vallen (zoals in 2027) of 29 december al in week 1 (zoals in 2025).

## Outlook-koppeling

**Alleen lezen.** Het programma maakt nooit afspraken aan, wijzigt niets en verwijdert niets. `build.ps1` controleert bij elke build dat de Outlook-code geen schrijvende aanroepen bevat. Klik je op een afspraak, dan opent die in een Outlook-venster. Wat je daar doet, gebeurt in Outlook zelf.

**Hoe het werkt.** Het programma leest de standaardagenda van *klassiek* Outlook via de lokale COM-koppeling, inclusief terugkerende afspraken. Dat gebeurt op een achtergrondthread, dus de agenda blijft altijd bedienbaar. Afspraken worden per maand 5 minuten bewaard. De maand ervoor en erna worden alvast opgehaald, zodat bladeren direct gaat.

**Nieuwe Outlook.** Op deze pc staan beide versies. Nieuwe Outlook heeft geen lokale koppeling. Daarom start WeekKalender klassiek Outlook onzichtbaar op de achtergrond als het nog niet draait. Na 5 minuten zonder gebruik laat het programma Outlook weer los, waarna Outlook zichzelf afsluit. Een Outlook dat je zelf open hebt, wordt nooit afgesloten.

Omdat je vooral nieuwe Outlook gebruikt, loopt klassiek Outlook soms achter. Na het starten haalt Outlook eerst de laatste wijzigingen van de server. Je ziet dan onderin "Outlook is net gestart en werkt de agenda nog bij." en de agenda ververst zichzelf elke 12 seconden tot dat klaar is.

**Als het niet lukt**, werkt de agenda gewoon door met weeknummers. De lijst toont dan een korte melding, bijvoorbeeld:
- Outlook reageert niet (er staat een melding of dialoogvenster open in Outlook);
- Outlook weigert de toegang (beveiligingsmelding geweigerd);
- klassiek Outlook is niet geïnstalleerd.

### Geen klassiek Outlook? Gebruik een .ics-agenda

Microsoft Graph (de online koppeling met Microsoft 365) zit er bewust niet in. Daarvoor is op een werkaccount bijna altijd een app-registratie en toestemming van een beheerder nodig. Het alternatief zonder IT:

1. **Agenda publiceren** in Outlook op het web (ook nieuwe Outlook): *Instellingen → Agenda → Gedeelde agenda's → Een agenda publiceren*. Kies je agenda en "Kan alle details weergeven", klik op *Publiceren* en kopieer de **ICS**-link.
   Let op: iedereen met die link kan je agenda inzien. Je organisatie kan publiceren ook hebben uitgeschakeld.
2. Of **exporteer een .ics-bestand** vanuit klassiek Outlook (*Bestand → Agenda opslaan*). Dat is wel een momentopname die niet vanzelf bijwerkt.
3. Zet de link of het pad in `instellingen.ini` (zie hieronder) bij `IcsBron=`.

Met `Bron=auto` gebruikt WeekKalender klassiek Outlook en valt het terug op de .ics-bron als dat niet lukt. Met `Bron=ics` gebruikt het alleen de .ics-bron. Een link wordt hooguit eens per 5 minuten opgehaald. Dit is de enige netwerkverbinding die WeekKalender zelf maakt, en alleen als je een link invult.

## Instellingen en bestanden

Alles staat in je eigen gebruikersmap, `%APPDATA%\WeekKalender\`:

- `instellingen.ini`: wordt bij de eerste start aangemaakt, met uitleg per regel. Wijzigingen gelden vanaf de volgende keer dat je de agenda opent.
  - `Bron=auto | outlook | ics | geen`
  - `IcsBron=` pad naar een .ics-bestand of een `https://`/`webcal://`-link
  - `OutlookStarten=ja | nee` (nee: alleen lezen als klassiek Outlook al open is)
  - `CacheMinuten=5`
- `log.txt`: korte technische meldingen, zonder inhoud van afspraken.

Autostart zet een snelkoppeling `WeekKalender.lnk` in je map Opstarten (`shell:startup`, geen adminrechten nodig). De eerste versie gebruikte de Run-sleutel in het register, maar op deze werkplek sloeg Windows die bij het aanmelden zonder melding over. Een oude Run-waarde zet het programma bij de volgende start automatisch om. Heb je WeekKalender in Taakbeheer bij *Opstarten* uitgeschakeld, dan staat het vinkje uit en zet het menu het weer aan. Verplaats je de exe, zet autostart dan opnieuw aan.

Er draait altijd maar één WeekKalender. Start je hem nog een keer, dan opent de agenda van het programma dat al draait. Geen telemetrie, geen updates van internet.

### Opdrachtregel

- `WeekKalender.exe --toon`: starten en meteen de agenda tonen
- `WeekKalender.exe --afsluiten`: een draaiende WeekKalender netjes laten stoppen (handig voordat je de exe vervangt)

## Bouwen

Nodig: Windows 10/11 en de C#-compiler uit Visual Studio Build Tools (staat op deze pc). Er worden geen pakketten gedownload.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Dit maakt `dist\WeekKalender.exe`, voert de alleen-lezen-controle uit en draait de tests (52 stuks: ISO-weeknummers met jaargrenzen, week 53 en schrikkeljaren tegen een onafhankelijke berekening voor elke dag van 1900 t/m 2100, maandraster, weekinvoer, .ics en herhalingsregels, schermplaatsing, cache, time-outs en foutafhandeling als Outlook ontbreekt, hangt of weigert). `-SkipTests` slaat de tests over.

`build\WeekKalender.Tests.exe --outlook` leest je echte Outlook-agenda en toont alleen aantallen en tijden, geen onderwerpen. `tools\make-icon.ps1` maakt het programma-icoon `src\app.ico` opnieuw.

Voor het testen van het uiterlijk: `WEEKKALENDER_THEMA=licht` of `donker` overschrijft de Windows-instelling. `WEEKKALENDER_GPU=1` zet WPF-hardwareversnelling aan. Standaard staat die uit, wat ongeveer 30 MB geheugen scheelt.

### Waarom deze techniek

WPF op .NET Framework 4.8.1, gecompileerd als één klein .exe-bestand. Het framework zit in Windows, dus er hoeft niets bij geïnstalleerd te worden en het bestand blijft ~130 KB in plaats van 70+ MB bij een zelfstandige .NET 8-app. Electron zou honderden MB kosten. Outlook-COM werkt er direct mee. WPF geeft afgeronde hoeken, schaduw en de acrylic-achtergrond van Windows 11 (via DWM), en schaalt per monitor (100/125/150/200%). Het icoon en het rechtsklikmenu zijn native Windows-onderdelen, zodat ze er net zo uitzien als die van Windows zelf.

Geheugen (gemeten op deze pc): ongeveer 53 MB privégeheugen. Het werkgeheugen dat Taakbeheer toont is ongeveer 34 MB met de popup open en zakt naar ~1 MB zodra de popup 20 seconden dicht is. De popup staat binnen ~0,3 s in beeld.

### Opbouw

```
src\Core\       weeknummers, maandraster, weekinvoer, .ics-lezer, cache, instellingen, autostart (zonder UI, getest)
src\Providers\  Outlook-COM (alleen lezen), .ics-bron, achtergrondthread met time-out en cache
src\App\        tray-icoon, menu, popup, thema, schermplaatsing
tests\          tests en de --outlook-controle
```

## Wat niet automatisch is getest

- Andere schermschalingen dan 100% en meerdere monitoren: het icoon is op 16/20/24/32 px gecontroleerd en de plaatsing (taakbalk onder/boven/links/rechts, automatisch verbergen, tweede monitor) zit in de tests. Een echte 150%-monitor is niet gebruikt.
- Middernacht en slaapstand: het icoon controleert elke 30 seconden de datum en reageert direct op ontwaken en tijdwijzigingen. Een echte nacht is niet doorlopen.
- Muiswiel: gebruikt dezelfde maandwissel als de knoppen en PageUp/PageDown, die wel zijn getest.
- Windows 10: werkt, maar zonder acrylic-achtergrond (effen kleur).
