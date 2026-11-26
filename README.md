# GameTranslator IT

Software desktop Windows che rileva i sottotitoli dei videogiochi in 58 lingue, li traduce in qualsiasi altra lingua supportata in tempo reale e mostra il risultato in un overlay trasparente sopra il gioco.

**Stato:** FASE 1–10 completate. EXE classico, MSIX firmato, copia portable (anche con tutti i 100 modelli inclusi), aggiornamenti automatici via GitHub Releases e supporto completo alle lingue richieste (versione 1.0.9).

---

## 1. Requisiti

- Windows 10 versione 19041 o successiva / Windows 11 (x64).
- Pacchetto lingua OCR `en-US` installato (Impostazioni → Lingue → Aggiungi una lingua).
- Almeno una voce TTS italiana installata per la lettura vocale.
- Per la traduzione locale: Python 3.10+ (lo script `installer\LibreTranslate\SetupLibreTranslate.ps1` crea l'ambiente virtuale e installa LibreTranslate/Argos). Nell'EXE classico serve Internet **solo la prima volta** per scaricare i 100 modelli disponibili (50 lingue). Le edizioni portable `Completo` e `Essenziali` includono già runtime e modelli; la portable standard può ricomporre i modelli dagli archivi `GameTranslatorIT-Models` della release.
- Per leggere sottotitoli in una lingua diversa da inglese/italiano serve il corrispondente pacchetto OCR di Windows (Impostazioni → Lingue → Aggiungi una lingua). Il pulsante **Installa altre lingue OCR di Windows…** apre direttamente quella pagina.

---

## 2. Compilazione e avvio

```powershell
# Build
dotnet build GameTranslatorIT.sln

# Avvio in sviluppo
dotnet run --project src\GameTranslatorIT.App\GameTranslatorIT.App.csproj

# Pubblicazione self-contained
.\installer\publish.ps1

# Pacchetti di rilascio completi (EXE + ZIP + MSIX + .sha256)
.\BuildRelease.ps1

# Cambia versione in un solo punto, poi ricostruisci
.\Set-Version.ps1 -Version 1.0.1
.\BuildRelease.ps1 -Version 1.0.1
```

Eseguibile pubblicato: `dist\GameTranslatorIT\GameTranslatorIT.App.exe`.
I pacchetti pronti per GitHub finiscono in `dist\Releases\v<versione>\`.

L'app avvia automaticamente il servizio di traduzione locale usando host e porta configurati in **Impostazioni → LINGUE E TRADUZIONE** (predefinito `http://127.0.0.1:5000`). Premere **Verifica traduzione locale** per controllarlo; se non è raggiungibile, la finestra principale mostra il motivo e ritenta l'avvio quando si cambia URL o porta.

Nel pannello **GIOCO** si può scegliere il monitor oppure la finestra reale del gioco, usare **Trova il gioco attivo** o lasciare attivato **Rileva automaticamente il gioco in primo piano**. La finestra viene identificata da handle e PID, non solo dal titolo: se il titolo cambia o due finestre hanno lo stesso nome, l'area di cattura resta corretta. Monitor e finestre vengono aggiornati automaticamente ogni pochi secondi.

Dalla finestra principale, nel pannello **DISPOSITIVI IN USCITA**, si può scegliere l'uscita audio, la **voce TTS installata in Windows**, il **microfono/ingresso audio** rilevato dal sistema oppure spuntare **Riproduci la voce su tutte le uscite attive**. Nel pannello **CONTROLLO** si può regolare il volume. Le modifiche vengono salvate subito e si applicano dalla frase successiva; velocità e altre opzioni restano in **Impostazioni → VOCE**.

---

## 3. Funzionalità implementate

| Area | Implementazione reale |
|---|---|
| Acquisizione | GDI `BitBlt` + `GetDIBits` su monitor selezionato o finestra di gioco reale (handle + PID, DPI-aware); hash FNV-1a dei pixel per rilevare le modifiche (solo le modifiche vanno a OCR). Rilevamento automatico della finestra in primo piano. |
| OCR | `Windows.Media.Ocr.OcrEngine` con ritaglio automatico della fascia sottotitoli (o area manuale 0–1), ingrandimento e contrasto regolabili. Tutte le 58 lingue sono selezionabili: se il pacchetto Windows manca, l'app usa un riconoscitore installato e avvisa, invece di bloccare la traduzione. |
| Deduplica | Cache degli ultimi 200 testi normalizzati; nessuna ritraduzione di frasi già viste; coda "latest-wins" che scarta traduzioni obsolete. |
| Traduzione | `CompositeTranslationProvider`: usa sempre `LibreTranslateProvider` locale (`POST /translate`, `q`/`source`/`target`) quando il modello esiste; solo per le 8 lingue senza modello e solo con consenso esplicito usa `OnlineTranslationProvider`. Glossario `chiave=valore` e contesto delle ultime 3 frasi. |
| Overlay | Finestra layered WPF (`WS_EX_LAYERED`, `WS_EX_NOACTIVATE`, `WS_EX_TRANSPARENT`, `WM_NCHITTEST → HTTRANSPARENT`), sempre in primo piano, spostabile/ridimensionabile in modalità modifica, testo IT o EN+IT. |
| Voce e dispositivi | `SpeechSynthesizer` con qualunque voce Windows installata selezionabile (predefinita: prima italiana); volume/velocità; uscita instradata tramite `AudioGraph` su un dispositivo scelto o, in parallelo, su tutte le uscite attive. Microfoni/ingressi audio enumerati da Core Audio e WinRT. La lettura non blocca la visualizzazione testuale. |
| Fallback schermo intero | Monitor secondario e/o lettura vocale forzata configurabili; avviso esplicito nell'UI. |
| Impostazioni | Salvataggio JSON in `%APPDATA%\GameTranslatorIT\settings.json`; ogni controllo è collegato a un comportamento reale. |

---

## 4. Limitazioni tecniche reali

1. **Schermo intero esclusivo**: una finestra normale, anche topmost, non può essere composta sopra il gioco in true exclusive fullscreen. L'overlay non è affidabile in quella modalità. Usare "finestra senza bordi" (borderless) oppure il fallback su monitor secondario / sola voce. Non è stato scelto l'injection nel processo di gioco perché è fragile e spesso bloccato dagli anti-cheat.
2. **Cattura GDI**: legge il frame composto sul desktop. Funziona in finestra e borderless; in exclusive fullscreen può restituire contenuto vuoto. WGC resta un'evoluzione possibile ma richiede interoperabilità Direct3D11 non verificata con librerie di terze parti: non è stata introdotta per rispettare il vincolo "non inventare librerie".
3. **Pacchetto MSIX**: dichiara solo `runFullTrust`, necessario per un'applicazione desktop full-trust. Le capability di Windows Graphics Capture non sono dichiarate perché la cattura usa GDI.
4. **OCR**: Windows OCR può sbagliare con font decorativi o sfondi estremi. Sono disponibili area manuale, ingrandimento e contrasto.
5. **Traduzione locale**: qualità inferiore a servizi online e latenza circa 0,5–2 s per frase; la coda latest-wins evita accumuli.
6. **Voci TTS**: dipendono dalle voci installate in Windows; l'app segnala se non è presente alcuna voce italiana.
7. **Anti-cheat**: alcuni titoli bloccano qualsiasi forma di cattura: nessuna soluzione garantita.

---

## 5. Installatori (FASE 9)

Tutti gli output sono in `dist\installer\`.

### EXE classico — scegli tu la cartella

`GameTranslatorIT-Setup-1.0.9.exe` (circa 55 MB), interfaccia solo in italiano.
Durante l'installazione compare la pagina **Cartella di destinazione**: puoi scegliere qualsiasi cartella. Il predefinito è `%LOCALAPPDATA%\Programs\GameTranslatorIT`, scrivibile senza diritti di amministratore.
Al termine puoi installare il motore di traduzione locale e avviare l'app.

Ricostruzione:

```powershell
.\installer\publish.ps1
& "C:\Users\corci\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\InnoSetup\GameTranslatorIT.iss
```

### MSIX — firmato, ma senza scelta della cartella

`GameTranslatorIT.msix` (circa 77 MB) è firmato con un certificato di sviluppo self-signed generato in `GameTranslatorIT-dev.cer` e già importato tra i certificati attendibili dell'utente corrente.

- `installer\MSIX\InstallaMsix.cmd` installa il pacchetto (richiede elevazione).
- **Windows non permette di scegliere una cartella arbitraria per un MSIX**: il pacchetto va nel negozio dei pacchetti di Windows. Per scegliere la cartella usa l'EXE classico o la copia portable.

Ricostruzione (makeappx/signtool arrivano dal pacchetto NuGet, senza installare il Windows SDK):

```powershell
dotnet restore installer\MSIX\MsixTools.csproj
.\installer\MSIX\build-msix.ps1
```

### Copia portable — scegli tu la cartella

- `GameTranslatorIT-Portable-1.0.9.zip`: cartella `GameTranslatorIT` con app, runtime LibreTranslate autonomo (Python incluso) e launcher. Nessuna installazione, nessun registro modificato. Se accanto alla cartella estrai anche gli archivi `GameTranslatorIT-Models-1.0.9.part*.zip`, l'app li verifica (SHA256) e li estrae automaticamente in `Models\argos-translate\packages` al primo avvio; altrimenti usa i modelli installati nel profilo utente.
- `GameTranslatorIT-Portable-Essenziali-1.0.9.zip`: stessa portable con già incluse le coppie italiano, spagnolo, francese, portoghese e cinese ↔ inglese (funziona subito offline).
- `GameTranslatorIT-Portable-Completo-1.0.9.zip`: portable con **tutti i 100 modelli** (~10 GB). Viene generata solo in locale e non è pubblicata su GitHub perché supera il limite di 2 GB per asset.

`installer\Portable\CopiaPortable.cmd` fa la stessa cosa con una finestra di selezione cartella.

---

## 6. Aggiornamenti automatici e pubblicazione

### Come funzionano gli aggiornamenti

La versione è definita una sola volta in `Directory.Build.props`. `Set-Version.ps1` allinea automaticamente assembly, installer Inno, manifest MSIX e README.

All'avvio (dopo 8 secondi) e a intervalli configurabili l'app chiama:

```
GET https://api.github.com/repos/squalo8410/GameTranslatorIT/releases/latest
```

- Per un **repository privato** puoi inserire un token personale in **Impostazioni → AGGIORNAMENTI → Token GitHub** (salvato solo nel profilo Windows dell'utente, mai negli asset distribuiti). In alternativa imposta la variabile `GITHUB_TOKEN` prima di avviare l'app:

  ```powershell
  setx GITHUB_TOKEN "<TOKEN_PERSONALE_SOLO_LETTURA_CONTENTS>"
  ```

  Il token deve avere accesso al repository e il permesso **Contents: read**. La variabile `GITHUB_TOKEN`, se presente, ha la priorità sul campo delle impostazioni. Non va mai scritto nel codice, nei file distribuiti o nel repository. Dopo `setx` chiudi e riapri l'app (e, se necessario, Explorer). Senza token l'app continua a tradurre: mostra solo un avviso nel log.

- Vengono ignorate le release `draft` e `prerelease`; è richiesto un tag `vX.Y.Z`. Una bozza privata non viene scaricata automaticamente nemmeno con token: GitHub esclude le bozze da `/releases/latest`. Per l'aggiornamento autonomo serve una release **pubblicata**.
- L'asset viene scelto in questo ordine: **installer EXE → ZIP portable → MSIX**.
- Per ogni asset la release deve contenere il file `.sha256` corrispondente. L'app scarica in una cartella temporanea, verifica dimensione e SHA256, e **non avvia nulla se il checksum non torna**. Se è impostata `GITUPDATER_CERT_THUMBPRINT`, verifica anche la firma Authenticode dell'EXE.
- Per l'EXE viene avviato l'installer con `/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /DIR="<cartella corrente>"`: la cartella scelta dall'utente non cambia. Per lo ZIP la cartella viene sostituita con backup e rollback in caso di errore. Per l'MSIX viene usato `Add-AppxPackage -ForceUpdateFromAnyVersion` (serve pacchetto firmato ed elevazione).
- Prima dell'installazione l'app chiude pipeline, overlay, hotkey e TTS; uno script esterno attende la chiusura, applica l'aggiornamento e riavvia l'app.
- `settings.json` e i modelli LibreTranslate sono in `%APPDATA%` / `%LOCALAPPDATA%`: non vengono toccati né dalla disinstallazione né dall'aggiornamento.
- La finestra **Aggiornamento disponibile** mostra note, avanzamento e pulsanti **Ignora questa versione**, **Apri pagina release**, **Chiudi**, **Scarica e installa**.
- In **Impostazioni → AGGIORNAMENTI** si può disattivare il controllo all'avvio e scegliere l'intervallo (1–168 ore; predefinito 6).

### Preparare una release

```powershell
.\Set-Version.ps1 -Version 1.0.1
git add -A
git commit -m "Release 1.0.1"
git tag v1.0.1
git push origin main --tags

# Build locale completa: scrive su G:\GameTranslatorIT-Releases e mette la portable completa in Downloads
.\BuildRelease.ps1 -Version 1.0.7 -ReleaseOutputPath 'G:\GameTranslatorIT-Releases' `
    -FullPortableOutputPath "$env:USERPROFILE\Downloads" -PfxPath '...' -PfxPassword '...'
```

Il workflow `.github/workflows/release.yml` pubblica la release quando viene pushato un tag `v*`. Pubblica EXE, portable standard, MSIX (se disponibile) e i relativi `.sha256` con la `GITHUB_TOKEN` integrata di Actions. I modelli da 10 GB non vengono costruiti in CI (`-SkipModelAssets`): dopo il workflow carica dalla build locale, con `gh release upload <tag> ... --clobber`, la portable standard aggiornata (contiene il manifest), `GameTranslatorIT-Portable-Essenziali-*`, `GameTranslatorIT-Models-*.part*.zip` e tutti i loro `.sha256`.

Per includere l'MSIX firmato configura due secret del repository:

- `MSIX_PFX_BASE64`: contenuto del PFX in Base64 (fra i secret di Actions).
- `MSIX_PFX_PASSWORD`: password del PFX.

Il PFX **non viene mai pubblicato** tra gli asset. Se i secret mancano, il workflow rilascia comunque EXE e ZIP. L'MSIX deve avere la stessa identità/publisher del precedente e una versione superiore; l'installazione richiede elevazione.

Carica nella release anche un `GameTranslatorIT-dev.cer` solo per il sideloading locale: per la produzione usa un certificato Authenticode affidabile e, se vuoi la verifica in app, imposta `GITUPDATER_CERT_THUMBPRINT` sul PC con l'impronta del certificato.

---

## 7. Documentazione ufficiale verificata

- WinRT in app desktop .NET: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-com-interop-csharp
- Windows.Graphics.Capture: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureitem?view=winrt-26100
- Windows.Media.Ocr: https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine?view=winrt-26100
- SoftwareBitmap.CreateCopyFromBuffer: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.softwarebitmap.createcopyfrombuffer?view=winrt-26100
- SpeechSynthesizer: https://learn.microsoft.com/en-us/uwp/api/windows.media.speechsynthesis.speechsynthesizer?view=winrt-26100
- AudioGraph / PrimaryRenderDevice: https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.audiographsettings.primaryrenderdevice?view=winrt-26100
- Overlay Win32: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
- LibreTranslate API: https://docs.libretranslate.com/
- MSIX: https://learn.microsoft.com/en-us/windows/msix/overview

---

## 8. Struttura

```
src/GameTranslatorIT.App       UI WPF, MVVM, pipeline, overlay host, TTS
src/GameTranslatorIT.Core      modelli, impostazioni JSON, interfacce
src/GameTranslatorIT.Capture   enumerazione monitor/finestre, cattura GDI
src/GameTranslatorIT.Ocr       preprocessing + Windows.Media.Ocr
src/GameTranslatorIT.Translation  provider LibreTranslate locale
src/GameTranslatorIT.Overlay   finestra layered click-through
src/GameTranslatorIT.Tts       voci italiane + AudioGraph
installer/                     publish, Inno Setup, MSIX, portable, setup LibreTranslate
```

---

## 9. SUPPORTO COMPLETO A TUTTE LE LINGUE

Sono selezionabili **58 lingue**: le 55 richieste più azero, esperanto e kirghiso già presenti nel motore locale.

### Motore locale (50 lingue, nessun dato inviato online)

`albanese, arabo, azero, basco, bengalese, bulgaro, catalano, cinese semplificato, cinese tradizionale, ceco, danese, olandese, esperanto, estone, finlandese, francese, galiziano, tedesco, greco, ebraico, hindi, ungherese, indonesiano, irlandese, inglese, italiano, giapponese, coreano, kirghiso, lettone, lituano, malese, norvegese, persiano, polacco, portoghese, portoghese brasiliano, rumeno, russo, slovacco, sloveno, spagnolo, swahili, svedese, filippino, thai, turco, ucraino, urdu, vietnamita`.

I 100 modelli Argos vengono scaricati automaticamente da `SetupLibreTranslate.ps1` al primo avvio dell'EXE classico e salvati in `%LOCALAPPDATA%\GameTranslatorIT\LibreTranslate` (tramite `%USERPROFILE%\.local\share\argos-translate\packages`): l'EXE resta leggero. Nelle portable i modelli sono invece inclusi (Completo/Essenziali) oppure ricomposti dagli archivi della release; l'app avvia LibreTranslate con `ARGOS_PACKAGES_DIR` puntato alla cartella `Models` della portable. I 100 modelli occupano circa 9,8 GB, per questo su GitHub vengono pubblicati come più archivi separati.

### Fallback online esplicito (8 lingue)

Islandese, croato, serbo, macedone, tamil, telugu, gallese e latino **non hanno alcun modello nell'indice Argos ufficiale**: l'app non li dichiara mai "locali". Per tradurli occorre spuntare in **Impostazioni → LINGUE E TRADUZIONE → Fallback online**. In quel caso, e solo per una coppia non coperta dal modello locale, il testo viene inviato a un servizio online. Senza il consenso l'app mostra un errore chiaro invece di tradurre male o fingere supporto.

### Rilevamento automatico e cambio lingua

- La voce **Rilevamento automatico della lingua** è la prima del menu sorgente: il motore locale usa `source=auto` (API `/detect` verificata) e rileva la lingua dalla frase letta.
- Windows deve comunque sapere **quale alfabeto leggere dallo schermo**: per questo esiste il menu separato **Lingua OCR (lettura)**. Se la sorgente è esplicita, la lettura segue automaticamente quella lingua.
- Lingue sorgente, destinazione e OCR possono essere cambiate mentre la traduzione è in funzione: la pipeline sostituisce il riconoscitore e il provider senza riavviare l'app e senza interrompere la cattura.

### Caratteri speciali e scrittura da destra a sinistra

Le richieste usano UTF-8 (form-urlencoded o query codificata). L'overlay adotta un elenco di font con fallback (`Segoe UI, Microsoft YaHei, Arial Unicode MS, Tahoma, SimSun, Yu Gothic, Malgun Gothic, Noto Sans`) e imposta `FlowDirection=RightToLeft` sull'intero blocco del riquadro quando il testo contiene caratteri arabi, ebraici, persiani o urdu, mantenendo la direzione corretta anche per il testo originale: niente quadratini e ordine di lettura corretto.

### Verifiche eseguite prima della release

- Menu sorgente: 58 lingue + auto; menu destinazione: 58 lingue.
- Le 50 lingue locali dichiarate sono tutte presenti in `/languages` del servizio reale.
- Le 8 lingue online sono state tradotte una per una con esito non vuoto.
- `source=auto` traduce correttamente (inglese, giapponese, arabo, russo).
- Con fallback disattivato le 8 lingue vengono rifiutate con un messaggio in italiano.
- Cambio lingua e provider applicati a caldo, senza riavvio dell'applicazione.
