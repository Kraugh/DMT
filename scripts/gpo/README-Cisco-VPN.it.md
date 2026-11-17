# Rimozione Cisco AnyConnect VPN, incluse le versioni Secure Client 5.1

`Remove-CiscoAnyConnectVpn.ps1` legge il registro HKLM a 32/64 bit e riconosce soltanto:

- Cisco Secure Client - AnyConnect VPN
- Cisco AnyConnect Secure Mobility Client
- Cisco AnyConnect VPN Client

Richiede publisher Cisco Systems e prodotto MSI con ProductCode GUID valido. Non rimuove Umbrella, NAM, ISE Posture o altri moduli Cisco. Non usa `Win32_Product`, `gpresult`, password salvate, cancellazioni manuali del registro o riavvii. La disinstallazione puo' arrestare i servizi VPN e impedisce l'uso futuro della VPN Cisco.

## Prima della distribuzione

Ritirare la distribuzione automatica di questo pacchetto dalle GPO/strumenti che lo installano, altrimenti puo' essere reinstallato. Non disabilitare indiscriminatamente altre impostazioni o moduli della stessa GPO. Il solo inventario non identifica quale sistema lo abbia distribuito.

Eseguire dalla postazione di gestione di dominio con sessione amministrativa; i PC offline/non raggiungibili restano da trattare. Per questi ultimi si puo' assegnare lo script all'avvio computer in una GPO di rimozione, applicata al gruppo/OU desiderato, con parametro `-Apply`. Il controllo locale rende la procedura idempotente. Rispettare le policy di firma dell'organizzazione, senza modificarle dallo script.

## Simulazione remota

Il pacchetto puo' includere `Invoke-CiscoVpnFleetRemoval.ps1`: avviandolo senza parametri esegue il piano sull'elenco `CiscoAnyConnect.targets.txt`; con `-Apply` esegue la rimozione. Entrambe le modalita' salvano un CSV univoco accanto allo script e copiano il riepilogo negli Appunti usando `Set-Clipboard` / `Get-Clipboard`. Richiede una cartella locale scrivibile. Il nome della destinazione e' passato a Kerberos, senza password in file o script.

Estrarre lo script e l'elenco di destinazioni nella stessa cartella. Da PowerShell amministrativa, nella cartella estratta:

```powershell
$targets = Get-Content .\CiscoAnyConnect.targets.txt | Where-Object { $_.Trim() } | Sort-Object -Unique
$results = foreach ($target in $targets) {
    try {
        Invoke-Command -ComputerName $target -Authentication Kerberos -FilePath .\Remove-CiscoAnyConnectVpn.ps1 -ErrorAction Stop
    } catch {
        [pscustomobject]@{Computer=$target;Status='UnreachableOrError';Message=$_.Exception.Message}
    }
}
$results | Export-Csv .\CiscoVPN-plan.csv -NoTypeInformation -Encoding UTF8
$results | Format-Table Computer,Product,Version,Status -AutoSize | Out-String -Width 250 | Set-Clipboard
Get-Clipboard
```

Il default non disinstalla nulla. `WouldRemove` e' una proposta; `WouldBlocked` richiede revisione. L'elenco deriva dagli ultimi inventari e puo' includere macchine non piu' raggiungibili: verificare gli esiti, non contare soltanto le righe dell'elenco.

## Applicazione remota

Usare lo stesso blocco sostituendo la riga Invoke-Command con:

```powershell
Invoke-Command -ComputerName $target -Authentication Kerberos -FilePath .\Remove-CiscoAnyConnectVpn.ps1 -ArgumentList $true -ErrorAction Stop
```

Salvare gli esiti in un nuovo `CiscoVPN-removal.csv` per conservare anche il piano. Il booleano posizionale valorizza `Execute`; l'opzione `-Apply` resta disponibile per l'esecuzione locale/GPO. Nessun comando remoto e' stato eseguito durante lo sviluppo.

## Esiti

- `Absent`: prodotto non rilevato localmente.
- `RemovedVerified`: uninstall concluso con codice accettato e ProductCode non piu' presente nel registro.
- `RestartRequiredVerifyAfterRestart`: codice MSI 3010; nessun riavvio viene comandato. Verificare dopo un riavvio manuale concordato.
- `FailedOrStillPresent`: errore MSI oppure prodotto ancora registrato.
- `ChangedSincePlanning`: il prodotto e' cambiato tra pianificazione ed esecuzione; non e' stato rimosso con il vecchio piano.
- `active_cisco_vpn_adapter_removal_deferred`: adattatore Cisco VPN attivo; nessuna rimozione. Riprovare su LAN/VPN alternativa dopo disconnessione concordata. Anche il fallimento del controllo adattatori impedisce la rimozione.
- `UnreachableOrError`: la connessione o il comando remoto non e' riuscito; non equivale a rimozione.

Log MSI: `%ProgramData%\DMT\CiscoVpnRemoval\<identificativo>\uninstall.log` su ciascun PC trattato. Parametri: `/x {ProductCode} /qn /norestart REBOOT=ReallySuppress /L*v ...`. Non vengono terminati manualmente processi ne' cancellati file/chiavi per forzare una rimozione fallita. Una nuova raccolta MT e importazione DMT confermera' l'inventario, senza sostituire la verifica operativa.

Fonti: [Cisco: rimozione moduli installati](https://www.cisco.com/c/en/us/support/docs/security/anyconnect-secure-mobility-client/217585-methods-to-uninstall-anyconnect-modules.html), [Microsoft: msiexec](https://learn.microsoft.com/windows-server/administration/windows-commands/msiexec).

Verifica: `pwsh -NoProfile -File tests/Test-CiscoVpnRemoval.ps1`. Test sintetici con registro, installer e privilegi simulati; nessuna disinstallazione reale. Parser Windows PowerShell 5.1 controllato separatamente.
