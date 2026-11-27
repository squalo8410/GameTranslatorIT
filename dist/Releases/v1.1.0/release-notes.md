# GameTranslator IT 1.1.0

Release pubblica successiva a `v1.0.9`. L'aggiornamento automatico la rileva e può applicare il differenziale senza reinstallare l'app. La release `v1.0.9` resta disponibile e invariata.

## Novità rispetto a 1.0.9

- **Aggiornamento differenziale**: manifest con hash e dimensioni, confronto con la cartella installata, download solo dei file nuovi/modificati, verifica SHA256, sostituzione nella stessa cartella, backup temporaneo e rollback automatico. Per i file bloccati usa un updater esterno e conferma il commit all'avvio successivo.
- **Fonte aggiornamenti configurabile**: URL del manifest impostabile in `Impostazioni > AGGIORNAMENTI` (o `GITUPDATER_MANIFEST_URL`). Il token GitHub, se configurato, viene inviato solo agli host GitHub; nessun token è incluso negli asset.
- **`INSTALLA TUTTE LE LINGUE`**: installa solo le coppie mancanti, mostra progresso, conteggi e lingua corrente, segnala i fallimenti e permette di riprovare solo quelli senza reinstallare le lingue riuscite. Usa esclusivamente fonti già presenti localmente.
- **`ELIMINA SELEZIONE`**: rimuove in modo reale e permanente una sola selezione, con conferma, aggiornamento immediato di elenco/conteggi, gestione di file in sola lettura e ripristino in memoria se il salvataggio fallisce. Non elimina nulla mentre un aggiornamento è in corso.
- **Persistenza rafforzata**: `settings.json` in `%APPDATA%\GameTranslatorIT` (o accanto all'EXE nelle portable), salvataggio atomico, gestione dell'attributo ReadOnly, ID dei riquadri duplicati/vuoti corretti e file corrotto segnalato senza sovrascrittura.
- **Build riproducibile**: compilazione deterministica e `PathMap`:DLL e PDB distribuiti non contengono percorsi assoluti del PC di sviluppo.

## Asset pubblicati

- `GameTranslatorIT-Setup-1.1.0.exe`: installer EXE.
- `GameTranslatorIT-Portable-1.1.0.zip`: portable con runtime locale; usa i modelli già installati nel profilo.
- `GameTranslatorIT-Portable-Essenziali-1.1.0.zip`: **non pubblicata in questa release CI** (la build salta i modelli da ~10 GB). Resta disponibile `GameTranslatorIT-Portable-Essenziali-1.0.9.zip`; la versione 1.1.0 può essere generata in locale con `BuildRelease.ps1` senza `-SkipModelAssets`.
- `GameTranslatorIT-Delta-1.1.0.zip` e `GameTranslatorIT-1.1.0-(installer|portable)-manifest.json`: aggiornamento solo dei file cambiati.
- `GameTranslatorIT-1.1.0.msix`: incluso solo se il certificato di firma è configurato nel repository.
- File `.sha256` corrispondenti per la verifica integrità.

Gli archivi dei 100 modelli (`GameTranslatorIT-Models-1.0.9.part*.zip`) restano disponibili nella release `v1.0.9`; la build CI di questa versione non ricarica ~10 GB di modelli. La portable completa con tutti i modelli continua a essere generata solo in locale.

## Verifiche

Build Release con 0 avvisi e 0 errori; test isolati su manifest/delta, updater con commit e rollback, persistenza delle impostazioni ed eliminazione delle selezioni superati. Non sono stati eseguiti test su un secondo PC fisico pulito: la procedura è documentata nel README.

Le impostazioni utente in `%APPDATA%\GameTranslatorIT`, le lingue installate e le selezioni vengono conservate: non fanno parte del manifest e non vengono mai sostituite.
