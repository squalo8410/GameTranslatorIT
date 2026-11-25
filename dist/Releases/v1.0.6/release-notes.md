# GameTranslator IT 1.0.6

Correzioni alle selezioni della finestra principale:

- **Lingue**: l'elenco dei modelli locali si aggiorna automaticamente ogni 20 secondi e con il pulsante «Aggiorna elenco lingue». Se LibreTranslate è installato ma non risponde, l'app lo avvia da sola in background: le destinazioni non restano più segnate come «da installare» dopo il recupero del servizio.
- **Monitor e finestra di gioco**: le due combo non sono più disabilitate in base alla modalità corrente. Scegliere un monitor attiva subito la cattura a schermo intero; scegliere una finestra attiva subito la cattura della finestra di gioco.
- **Riquadri**: la selezione e il salvataggio dei riquadri restano invariati e continuano a riavviare la pipeline senza riavviare l'app.

Il cambio lingua resta immediato, senza riavvio. Funzionalità invariate: 58 lingue selezionabili, auto-detect sorgente, OCR separato, fallback online solo su consenso esplicito, OCR/TTS, volumi, riquadri 1–12, overlay Unicode/RTL, updater con verifica SHA256.

## Asset pubblicati
- GameTranslatorIT-Setup-1.0.6.exe (installer EXE)
- GameTranslatorIT-Portable-1.0.6.zip (copia portable)
- GameTranslatorIT-1.0.6.msix (pacchetto MSIX firmato, quando disponibile)
- File `.sha256` corrispondenti per la verifica integrità

Le impostazioni utente in `%APPDATA%\GameTranslatorIT` vengono conservate.
