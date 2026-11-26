# GameTranslator IT 1.0.7

Tutte le 58 lingue restano selezionabili, con rilevamento automatico della sorgente, cambio lingua senza riavvio e fallback online esplicito per le 8 lingue senza modello Argos.

## Novità di questa versione

- La portable standard include il runtime LibreTranslate e un manifest dei modelli.
- I 100 modelli Argos (50 lingue × 2 direzioni) vengono pubblicati come più archivi `GameTranslatorIT-Models-1.0.7.part*.zip`, ciascuno sotto il limite di 2 GB di GitHub.
- L'app verifica lo SHA256 degli archivi e li estrae automaticamente in `Models\argos-translate\packages` al primo avvio, senza scaricare nulla.
- Nuova portable `Essenziali` con le coppie italiano, spagnolo, francese, portoghese e cinese ↔ inglese già incluse: pronta offline.
- Nuova portable `Completo` con tutti i modelli (~10 GB), generata solo in locale perché supera il limite GitHub.
- L'installer EXE classico continua a scaricare i modelli al primo avvio (comportamento invariato).

## Asset pubblicati

- `GameTranslatorIT-Setup-1.0.7.exe` — installer EXE italiano.
- `GameTranslatorIT-Portable-1.0.7.zip` — portable con runtime e ripristino automatico dei modelli.
- `GameTranslatorIT-Portable-Essenziali-1.0.7.zip` — portable offline con modelli essenziali.
- `GameTranslatorIT-Models-1.0.7.partNN.zip` — archivi dei 100 modelli, da tenere accanto alla cartella `GameTranslatorIT` estratta.
- `GameTranslatorIT-1.0.7.msix` — pacchetto MSIX firmato (quando disponibile).
- File `.sha256` corrispondenti per ogni asset.

La portable completa con tutti i modelli non è pubblicabile su GitHub (10 GB > 2 GB per asset); viene creata in locale in `Downloads`.

Le impostazioni utente in `%APPDATA%\GameTranslatorIT` vengono conservate.
