# GameTranslator IT 1.0.9 — bozza privata

Questa release è una **bozza privata**: non è visibile al pubblico e non viene rilevata dall’aggiornamento automatico dell’app. La release pubblica `v1.0.8` resta invariata.

## Correzioni rispetto a 1.0.8

- Rilevamento del gioco dalla finestra realmente in primo piano, con handle + PID invece del solo titolo.
- Filtri per finestre chiuse, minimizzate o nascoste, esclusione dell’app e dei processi di sistema, coordinate DPI-aware.
- Aggiornamento automatico di monitor e finestre senza riavviare la cattura.
- Validazione corretta delle lingue sorgente/destinazione nel provider combinato: il fallback online resta limitato alle 8 lingue senza modello e solo con consenso esplicito.
- Tutte le 58 lingue selezionabili anche per l’OCR; combo lingue aggiornate quando cambiano fallback online, pacchetti OCR o URL del servizio locale.
- Avvio di LibreTranslate sulla stessa host/porta configurata per la verifica.
- Nomi reali dei monitor, microfoni/ingressi audio enumerati da Windows, voci TTS installate selezionabili e riproduzione su tutte le uscite attive.
- Rimozione delle preferenze audio hardcoded per dispositivi specifici.
- Hotkey estesi a F1–F12, RTL su tutto il blocco dell’overlay, finestra Impostazioni ridimensionabile, documentazione aggiornata.

## Asset

- `GameTranslatorIT-Setup-1.0.9.exe`: installer EXE.
- `GameTranslatorIT-Portable-1.0.9.zip`: portable con runtime locale; usa gli archivi modello qui sotto oppure i modelli già installati nel profilo.
- `GameTranslatorIT-Portable-Essenziali-1.0.9.zip`: portable offline con le coppie it/es/fr/pt/zh ↔ inglese.
- `GameTranslatorIT-Models-1.0.9.part*.zip`: 100 modelli Argos, ricomposti automaticamente dall’app.
- `GameTranslatorIT-1.0.9.msix`: pacchetto MSIX firmato localmente.
- File `.sha256` corrispondenti per la verifica integrità.

La portable completa con tutti i modelli (~10 GB) resta solo locale perché supera il limite GitHub di 2 GB per asset.

Le impostazioni utente in `%APPDATA%\GameTranslatorIT` vengono conservate.
