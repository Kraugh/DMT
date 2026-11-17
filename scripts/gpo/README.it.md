# Pulizia PDF24 per GPO computer

`Remove-OldPdf24.ps1` e' compatibile con Windows PowerShell 5.1 e PowerShell 7 su Windows. La versione di riferimento predefinita e' **11.31.1**, non una ricerca automatica dell'ultima release online.

## Prova senza modifiche

Aprire PowerShell sul PC pilota e lanciare:

```powershell
.\Remove-OldPdf24.ps1
```

Mostra le installazioni a livello macchina dai rami Uninstall HKLM a 32 e 64 bit. `Remove` indica una versione inferiore; `Keep` indica una versione uguale o successiva; `Blocked` indica dati non riconosciuti. Questa modalita' non crea file e non esegue installer. Non usa `Win32_Product`. Non legge gli inventari DMT per decidere cosa rimuovere: usa lo stato locale del PC al momento dell'esecuzione.

## Esecuzione tramite GPO

1. Copiare lo script in **Mostra file** della GPO, in Configurazione computer > Criteri > Impostazioni di Windows > Script > Avvio.
2. Nella scheda **Script di PowerShell** aggiungere `Remove-OldPdf24.ps1`.
3. Parametri (sostituire il percorso di esempio con l'MSI distribuito dalla propria organizzazione):

```text
-Apply -MinimumVersion 11.31.1 -InstallerPath "\\domain.example\SYSVOL\domain.example\scripts\pdf24-creator-11.31.1-x64.msi"
```

4. Eseguire come avvio **computer/SYSTEM**, con PowerShell a 64 bit. Il file e la cartella della distribuzione devono essere leggibili dagli account computer e scrivibili solo dagli amministratori autorizzati. Se il dominio richiede script firmati, firmarlo secondo la procedura dell'organizzazione; lo script non cambia le execution policy.
5. Collegare inizialmente la GPO al solo ambito pilota e verificare un avvio concordato. Poi controllare PDF24, stampante virtuale, versione installata e log.

Il pacchetto MSI assegnato nella stessa GPO continua a gestire le installazioni nuove. Lo script interviene solo quando rileva una versione precedente: non dipende dall'ordine tra assegnazione MSI e script di avvio. Se trova soltanto versioni attuali/successive non fa nulla. Non aggiungere una seconda copia della stessa procedura a un'altra GPO o a un'attivita' pianificata.

## Cosa fa con -Apply

- Blocca la pulizia se PDF24 e' aperto in una sessione utente: non termina processi ne' servizi a forza.
- Prima di rimuovere alcunche', copia l'MSI in una cartella locale riservata a SYSTEM/Administrators, confronta SHA-256 e controlla ProductName, Manufacturer e ProductVersion. La verifica identifica il pacchetto ma **non sostituisce l'attendibilita' e i permessi della sorgente amministrata**. Se la sorgente non e' raggiungibile, non disinstalla nulla.
- Rimuove i prodotti MSI mediante il loro ProductCode, con `/x /qn /norestart REBOOT=ReallySuppress`.
- Per i vecchi EXE supporta solo gli uninstaller Inno `uninsNNN.exe` nella cartella PDF24 sotto Program Files, con firma Authenticode valida di geek software, e usa `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Gli argomenti presenti nel registro non vengono eseguiti. Percorsi personalizzati, firme non verificabili o altri installer richiedono revisione manuale.
- Dopo un tentativo di rimozione installa o ripara la 11.31.1 dall'MSI gia' copiato. E' necessario per ripristinare file e stampante condivisi, anche se la GPO ha gia' installato la nuova versione. Tenta il ripristino anche in caso di errore parziale di disinstallazione; l'errore resta segnalato.
- Non esegue downgrade. Se una versione successiva alla destinazione convive con una precedente, si ferma prima della pulizia: occorre un MSI di ripristino corrispondente alla versione che si vuole conservare.
- Non elimina manualmente chiavi di registro, cartelle applicative, stampanti, impostazioni utente o cache MSI. Non ricerca installazioni per utente in HKCU/profili offline. Non aggiunge MST/proprieta' aziendali: se sono necessarie, integrare quelle opzioni prima del pilota.
- Controlla nuovamente il registro dopo l'intervento. La verifica completa della stampante e dell'applicazione resta parte del pilota; una nuova raccolta MT confermera' l'inventario in DMT.

## Log ed esiti

Ogni intervento mantiene MSI locale e log in `%ProgramFiles%\DMT-GpoTools\PDF24-<identificativo>\`: `cleanup.log`, `uninstall-N.log`, `install-repair.log`. La cartella non e' ripulita automaticamente per consentire recupero e diagnosi. E' accessibile a SYSTEM/Administrators. Gli errori di preflight vengono scritti anche nell'output PowerShell/GPO.

- `0`: simulazione, nessun intervento necessario oppure pulizia e verifica riuscite.
- `3010`: operazioni riuscite, installer segnala riavvio necessario. **Lo script non riavvia il PC.** Concordare un riavvio manuale; il motore GPO non mostra necessariamente questo codice all'utente.
- `1`: blocco, errore o verifica non riuscita. Non equivale a rimozione completata. Un'eventuale installazione gia' rimossa potrebbe richiedere recupero con l'MSI locale.

Non e' un rollback transazionale: un errore durante disinstallazione/installazione puo' lasciare PDF24 incompleto. Un caso con riavvio pendente puo' richiedere un successivo avvio e verifica. I parametri sopprimono i riavvii standard MSI/Inno; il comportamento effettivo dei pacchetti va confermato sul pilota. Nessun installer reale e' stato eseguito durante lo sviluppo di questo script.

## Fonti e verifica

- [Microsoft: opzioni MSI](https://learn.microsoft.com/windows/win32/msi/command-line-options)
- [Microsoft: codici Windows Installer](https://learn.microsoft.com/windows/win32/msi/error-codes)
- [PDF24: uninstaller Inno Setup](https://help.pdf24.org/de/foren/thema/pdf24-deinstallieren-per-kommandozeile/)
- [Inno Setup: parametri di disinstallazione](https://jrsoftware.org/ishelp/topic_uninstcmdline.htm)

Test sintetici, senza installer reali: `powershell.exe -NoProfile -File .\tests\Test-Pdf24Cleanup.ps1` (oppure `pwsh`). La sintassi e' stata controllata dal parser Windows PowerShell 5.1; i test funzionali con dipendenze simulate sono stati eseguiti con PowerShell 7. L'execution policy locale impedisce qui l'esecuzione di file tramite Windows PowerShell 5.1; non e' stata modificata. Il pacchetto MSI aziendale e gli uninstaller reali devono ancora essere verificati sul pilota.
