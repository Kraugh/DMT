# Installazione sul PC fisico — pilota locale 0.2

**STATO BLOCCATO (2026-11-17):** il primo tentativo e' rimasto fermo per circa un'ora su «Creazione delle directory programmi e dati DMT» ed e' stato interrotto. Causa non diagnosticata. Le istruzioni descrivono il flusso previsto, non un'installazione riuscita. Non rilanciare sopra lo stato parziale prima della diagnosi. Vedi [stato e ripresa](STATUS.md).

Questa versione installa DMT su Windows x64 senza richiedere il repository, Visual Studio, l'SDK o un'installazione separata di .NET. Il runtime e' incluso. E' una **prima installazione locale**, non un aggiornamento automatico di installazioni precedenti.

## Trasferimento dalla VM

Sono separati due pacchetti:

1. **Installer**: programmi WPF, servizio web, runtime, Caddy, lingue e manifest SHA-256. Non contiene dati aziendali.
2. **Migrazione privata**: `DMT-data.sqlite`, `DMT-migration.json` e informazioni della copia. Contiene l'inventario aziendale e va trattata come backup privato, senza aggiungerla al repository o a una release pubblica.

Copiare entrambi gli ZIP sul PC fisico ed estrarli **nella stessa cartella locale**. Non avviare i programmi direttamente dall'anteprima dello ZIP. Avviare `DMT-Setup.exe` con il proprio account Windows amministratore: la fase di scrittura richiede UAC. Per questo pilota l'elevazione deve usare lo stesso account dell'interfaccia, come avviene con il consenso UAC di un amministratore; l'elevazione con un diverso account non e' supportata dal canale privato dell'installer.

## Aggiornamento 0.2.0-local.3 — 2026-11-27

Il profilo solo locale richiede almeno 8 caratteri. Il riepilogo distingue il database sorgente da importare dalla copia definitiva in `%ProgramData%\DMT\DB\DMT-data.sqlite`. Il percorso in temp e' solo la sorgente: non va spostato manualmente nella destinazione finale prima dell'installazione. Dopo installazione riuscita e verifica dei dati, l'utente puo' rimuovere la cartella temporanea di estrazione; non e' una dipendenza del servizio. Conservare il backup privato separatamente. Le installazioni precedenti non vengono spostate automaticamente.

## Procedura

Per il candidato `0.2.0-local.2`, prima di riprovare sul PC dove il vecchio installer si era bloccato, eseguire nella cartella estratta:

```powershell
.\Get-DmtInstallationDiagnostics.ps1 | Set-Clipboard
Get-Clipboard
```

Il comando e' in sola lettura. Condividere il riepilogo privatamente, non in issue pubbliche. Non cancellare cartelle o servizi per aggirare il controllo di installazione esistente. I nuovi log continui si trovano in `%LOCALAPPDATA%\DMT\SetupLogs`; il percorso viene mostrato durante l'installazione. La causa del vecchio blocco richiede ancora conferma sul fisico.

- Conserva il percorso HTTPS locale e scegli una porta libera nel passaggio Rete (proposta iniziale 8443).
- Crea un amministratore **DMT** con password di almeno 8 caratteri. E' distinto dall'account Windows/di dominio.
- Nel riepilogo controlla la cartella dei report: usare un percorso UNC raggiungibile dal PC fisico, non la lettera di un disco mappato nella VM.
- Se il pacchetto di migrazione e' stato estratto nella stessa cartella, il database `DMT-data.sqlite` viene proposto automaticamente. In alternativa seleziona il file tramite Sfoglia. Lasciandolo vuoto, viene creato un inventario nuovo.
- Controlla il riepilogo e premi il pulsante di installazione. L'installer verifica i programmi, copia il database con SQLite Backup, registra e avvia il servizio, prepara HTTPS, aggiunge al computer la CA locale dedicata e verifica la risposta autenticata dell'installazione tramite il suo identificativo.
- Alla fine apri l'indirizzo mostrato, `https://localhost:<porta>`, e accedi con l'amministratore DMT appena creato.

Lo storico viene copiato: il database della VM e il file di migrazione restano intatti. La copia comprende inventari indicizzati, storico e JSON originali gia' acquisiti. I file ZIP nella share restano nella share. Usare la VM come riferimento finche' il controllo sul PC fisico non e' concluso.

## Gestione quotidiana

Dal menu Start, **DMT**, si apre il pannello locale (UAC):

- Avvia / Riavvia / Stop del servizio, senza riavviare Windows;
- Apri dashboard sull'indirizzo HTTPS configurato;
- Acquisisci e archivia report dalla share usando l'account Windows con cui gira il pannello;
- Ripristina password amministratore DMT: richiede privilegi Windows, riavvia il servizio attivo e invalida le sessioni precedenti.

Il servizio `DMT` parte automaticamente all'avvio di Windows e gira come **LocalService**, senza credenziali di dominio salvate. Caddy e' un processo figlio controllato dal servizio: viene terminato anche se il processo padre termina improvvisamente. Il servizio viene configurato per il riavvio in caso di guasti. La gestione resta disponibile anche a servizio fermo.

Il servizio locale non accede automaticamente alla share con l'identita' LocalService. Per questa installazione, acquisizione e compressione mensile vengono eseguite dal pulsante del pannello, con l'account Windows autorizzato alla share. Il pannello mostra avanzamento, esiti parziali e cartella dei giornali. La semplice apertura della dashboard non importa nuovi dati. Gli eventuali report mentre il PC e' spento restano nella share fino all'acquisizione successiva.

## Risorse installate

- `%ProgramFiles%\DMT\Manager`: pannello WPF e runtime.
- `%ProgramFiles%\DMT\Service`: servizio web, Caddy e runtime.
- `%ProgramData%\DMT\DB\DMT-data.sqlite`: database locale.
- `%ProgramData%\DMT\config`: configurazione e credenziale amministratore DMT derivata con PBKDF2; nessuna password in chiaro.
- `%ProgramData%\DMT\caddy`: CA locale, chiavi e certificati Caddy, con permessi limitati.
- `%ProgramData%\DMT\installation-manifest.json`: risorse gestite, servizio e impronta del certificato.
- Menu Start: collegamento **DMT**.

L'installer non aggiunge regole firewall, non apre l'accesso LAN e non modifica certificati sugli altri computer. La CA locale viene resa attendibile solo sul PC di installazione. La porta pubblica locale e il backend interno sono separati: aprire sempre l'URL HTTPS mostrato, non la porta HTTP interna.

## Diagnostica e limiti del pilota

In caso di errore, il programma mostra il motivo e il file continuo `%LOCALAPPDATA%\DMT\SetupLogs\DMT-Setup-<identificativo>.log`. La pulizia tenta di arrestare/eliminare solo il servizio appena creato, rimuovere solo il certificato appena aggiunto e annullare i file appena copiati. Eventuali dati runtime generati rimangono conservati; non viene promesso un rollback transazionale di Windows. Non forzare la reinstallazione sopra una configurazione esistente: questa versione la rifiuta per non sovrascrivere inventario o risorse.

Non sono ancora inclusi accesso LAN, certificati aziendali esistenti, aggiornamento in-place e disinstallatore completo. I programmi di questo pilota non sono firmati. Le lingue restano esterne; le nuove stringhe operative sono complete in italiano/inglese, con bozze inglesi negli altri pacchetti, incluso Klingon.

La compilazione e i test coprono inventario/archiviazione, credenziali, contenimento dei percorsi, HTTPS Caddy reale, autenticazione/CSRF/logout e terminazione del processo figlio. Le prove non registrano servizi ne' rendono attendibili CA nella VM: UAC, registrazione SCM, ACL LocalService e attendibilita' nel browser vanno verificati con la prima installazione sul PC fisico. Verificare infine conteggio computer, storico di un PC e una acquisizione dal pannello.
