# GameTranslator IT 1.0.8

Versione correttiva della 1.0.7: le edizioni portable ora includono anche il runtime Python e avviano LibreTranslate senza dipendere da percorsi assoluti presenti sulla macchina che ha creato il pacchetto. La 1.0.7 resta disponibile ma è superata.

## Correzioni

- Rimosso il wrapper `libretranslate.exe` generato da pip: le portable avviano il `python.exe` incluso con lo script `avvia-libretranslate.py` e `PYTHONHOME` puntato al runtime Python nella cartella `Runtime`.
- Il runtime Python è copiato dentro la portable: nessuna dipendenza da Python installato in `C:\Users\<utente>` o da una venv esistente.
- Confermati i 100 modelli Argos (50 lingue × 2 direzioni), il ripristino SHA256 degli archivi spezzati e la portable Essenziale offline con 10 modelli.

## Asset pubblicati

- `GameTranslatorIT-Setup-1.0.8.exe` — installer EXE italiano (scarica i modelli al primo avvio).
- `GameTranslatorIT-Portable-1.0.8.zip` — portable con runtime autonomo e manifest dei modelli.
- `GameTranslatorIT-Portable-Essenziali-1.0.8.zip` — portable offline con modelli essenziali già inclusi.
- `GameTranslatorIT-Models-1.0.8.partNN.zip` — archivi dei 100 modelli, da tenere accanto alla cartella `GameTranslatorIT` estratta.
- `GameTranslatorIT-1.0.8.msix` — pacchetto MSIX firmato (quando disponibile).
- File `.sha256` corrispondenti per ogni asset.

La portable completa con tutti i modelli (~10 GB) non è pubblicabile su GitHub (limite 2 GB per asset); viene creata in locale in `Downloads`.
