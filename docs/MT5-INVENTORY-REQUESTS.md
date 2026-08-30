# DMT → Maintenance Toolkit 5.0
## Richieste di implementazione Inventory / JSON Export

**Data:** 2026-08-30  
**Destinatario:** sviluppo Maintenance Toolkit 5.0  
**Origine requisiti:** progetto DMT — Dashboard Maintenance Toolkit  
**Baseline analizzata:** Maintenance Toolkit 4.0.0

---

# 1. Obiettivo

Maintenance Toolkit 5.0 deve mantenere invariata la propria funzione principale:

> mantenere la macchina Windows aggiornata, sana e verificata.

A questa funzione viene aggiunto un ruolo secondario di **technical inventory collector**.

MT 5.0 deve quindi produrre, a ogni esecuzione dell'inventory, uno snapshot JSON completo dello stato tecnico della macchina.

Il JSON dovrà poter essere utilizzato da DMT, ma **MT non deve dipendere da DMT** e non deve conoscere concetti gestionali quali tenant, sede, dipartimento, assegnatario, contratto o garanzia.

Principio architetturale:

```text
MT osserva la macchina
        │
        ▼
Inventory JSON
        │
        ▼
DMT interpreta, storicizza e gestisce
```

---

# 2. Situazione attuale MT 4.0

L'attuale modulo:

```text
app/modules/02_inventory.ps1
```

raccoglie già almeno:

- hostname;
- manufacturer;
- model;
- BIOS serial number;
- BIOS version;
- Windows caption/version;
- CPU;
- RAM totale;
- physical disks:
  - friendly name;
  - capacità;
  - media type;
  - bus type;
  - health status;
- elenco applicazioni ottenuto da Winget.

Produce attualmente:

```text
inventario_sistema.txt
winget_applicazioni.txt
```

MT 5.0 dovrà **estendere** questa funzione, evitando quando possibile di duplicare interrogazioni già effettuate da altri moduli.

---

# 3. Requisito fondamentale: uno snapshot ad ogni esecuzione

MT deve produrre **sempre un nuovo JSON** quando viene eseguito il modulo inventory.

Non deve verificare preliminarmente se l'inventory è cambiato.

Motivazioni:

- la raccolta prevista è relativamente rapida;
- i JSON risultanti sono piccoli;
- ogni esecuzione costituisce una fotografia temporale utile;
- DMT potrà ricostruire lo storico;
- DMT potrà stabilire successivamente quali variazioni meritano di essere conservate a lungo termine.

Ogni snapshot deve avere un identificatore univoco:

```json
"snapshotId": "UUID/GUID"
```

e un timestamp:

```json
"collectedAt": "2026-08-30T10:30:00+02:00"
```

Formato raccomandato: ISO 8601 con timezone.

---

# 4. Salvataggio locale

Il JSON deve essere salvato **sempre localmente**, seguendo il normale comportamento/reporting di MT.

La posizione deve essere coerente con l'attuale gestione delle sessioni/report di MT.

Il funzionamento standalone/casalingo di MT non deve richiedere alcuna share o infrastruttura DMT.

---

# 5. Destinazione remota opzionale tramite CLI

Per gli ambienti aziendali MT potrà essere lanciato tramite Active Directory / GPO.

Si richiede un nuovo parametro CLI opzionale, nome definitivo da concordare.

Esempio indicativo:

```powershell
MaintenanceToolkit.ps1 `
    -InventoryShare "\\SERVER\DMT\incoming"
```

o equivalente.

Comportamento:

```text
nessun parametro
    └─ JSON locale soltanto

-InventoryShare specificato
    ├─ JSON locale
    └─ copia aggiuntiva nella destinazione indicata
```

La share sarà stabilita dalla GPO/script di avvio.

MT **non deve conoscere**:

- tenant;
- site;
- reparto;
- assegnatario.

Il contesto organizzativo verrà attribuito da DMT in base alla cartella da cui il file viene acquisito.

---

# 6. La destinazione remota non deve essere obbligatoria

La mancata raggiungibilità della share non deve provocare il fallimento della manutenzione.

Flusso richiesto:

```text
raccolta inventory
       │
       ├─ scrittura locale
       │
       └─ copia remota best effort
                │
           success / warning
```

Se la copia remota fallisce:

- MT continua normalmente;
- l'errore viene registrato nel log;
- il JSON locale viene comunque mantenuto;
- il risultato del modulo non deve trasformarsi in errore fatale dell'intera manutenzione.

---

# 7. Scrittura remota sicura

Per evitare che DMT possa leggere file parzialmente scritti:

1. MT genera integralmente il JSON;
2. salva/copia inizialmente con estensione temporanea;
3. completa la scrittura;
4. rinomina il file in `.json`.

Esempio:

```text
PC001_20260830T103000_<GUID>.json.tmp
                         ↓
PC001_20260830T103000_<GUID>.json
```

DMT ignorerà i file `.tmp`.

Il rename finale dovrà essere effettuato solo dopo la conclusione della scrittura.

---

# 8. Naming dei file

Il filename deve essere utile all'operatore, ma **non deve costituire l'identità primaria dello snapshot**.

Formato indicativo:

```text
<hostname>_<timestamp>_<snapshotId>.json
```

Esempio:

```text
PC-AMM-023_20260830T103000_8d0c7c7d.json
```

Il `snapshotId` completo rimane comunque all'interno del JSON.

I caratteri del filename devono essere sanitizzati.

---

# 9. Versionamento dello schema

Ogni JSON deve contenere:

```json
"schemaVersion": "1.0"
```

La versione dello schema deve essere indipendente dalla versione di MT.

Esempio:

```json
"collector": {
  "name": "Maintenance Toolkit",
  "version": "5.0.0"
}
```

MT 5.1 potrà quindi continuare a produrre Inventory Schema 1.0 se il formato non cambia.

---

# 10. Principio di completezza

Il JSON deve essere **il più completo possibile**.

DMT deciderà:

- quali campi importare;
- quali indicizzare;
- quali mostrare;
- quali usare per allarmi/query;
- quali storicizzare.

MT non deve limitare la raccolta sulla base delle necessità immediate della prima versione di DMT.

Principio:

> Se un dato tecnico può essere raccolto in modo ragionevolmente affidabile, non contiene segreti e ha un possibile valore diagnostico/inventariale, è candidato all'export.

---

# 11. Identificazione della macchina

Esportare più identificatori indipendenti possibile.

Minimo richiesto:

- hostname;
- SMBIOS/System UUID;
- BIOS serial number;
- manufacturer;
- model.

Quando disponibile:

- OEM asset tag;
- chassis serial number;
- motherboard/baseboard serial;
- motherboard/baseboard product;
- chassis type.

Esempio concettuale:

```json
"deviceIdentity": {
  "hostname": "PC001",
  "systemUuid": "...",
  "serialNumber": "...",
  "manufacturer": "Dell Inc.",
  "model": "Latitude 5550",
  "assetTag": "..."
}
```

**MT non deve generare il DMT AssetId.**

DMT utilizzerà questi identificatori per determinare se uno snapshot appartiene ad un asset già noto.

---

# 12. Sistema operativo

Esportare almeno:

- product/caption;
- edition;
- version;
- feature release, quando determinabile;
- build;
- UBR/revision, quando disponibile;
- architecture;
- installation date, se affidabile;
- last boot time;
- uptime derivabile;
- system locale;
- Windows display version.

Esempio:

```json
"os": {
  "name": "Microsoft Windows 11 Pro",
  "edition": "Professional",
  "displayVersion": "24H2",
  "version": "10.0",
  "build": "26100",
  "ubr": 1234,
  "architecture": "x64"
}
```

---

# 13. Firmware / BIOS / UEFI

Esportare:

- manufacturer/vendor;
- BIOS/UEFI version;
- SMBIOS BIOS version;
- release date;
- SMBIOS version;
- firmware mode BIOS/UEFI;
- Secure Boot enabled/disabled/unknown;
- eventuali altri identificatori firmware affidabili.

Questo deve permettere in futuro query DMT come:

> Quali PC hanno ancora una determinata versione firmware?

oppure:

> Quali dispositivi non hanno Secure Boot attivo?

---

# 14. CPU

Esportare almeno:

- manufacturer;
- model/name;
- architecture;
- socket;
- physical cores;
- logical processors/threads;
- max clock, se disponibile;
- processor ID, se utile e affidabile.

Per sistemi multi-socket, prevedere una collection.

---

# 15. RAM

Esportare:

## Totale

- RAM totale installata;
- RAM visibile/usabile dal sistema.

## Moduli DIMM, quando disponibili

Per ciascun modulo:

- slot/device locator;
- bank;
- capacity;
- manufacturer;
- part number;
- serial number;
- configured speed;
- rated speed, se disponibile;
- memory type/form factor, quando determinabile.

Questo deve consentire a DMT di rilevare upgrade come:

```text
16 GB → 32 GB
```

e, possibilmente:

```text
2 x 8 GB → 2 x 16 GB
```

---

# 16. Storage fisico

Per ogni disco fisico raccogliere, quando disponibile:

- friendly name/model;
- manufacturer;
- serial number;
- firmware version;
- size;
- media type;
- bus type;
- health status;
- operational status;
- interface/protocol;
- SSD/HDD/NVMe quando determinabile.

L'attuale `Get-PhysicalDisk` può essere mantenuto come una delle fonti.

Dovrà essere valutata l'integrazione con CIM/WMI o altre API Windows per ottenere seriale e firmware dove `Get-PhysicalDisk` non li fornisce.

---

# 17. Volumi / filesystem

Per ogni volume significativo:

- drive letter/mount point;
- label;
- filesystem;
- total size;
- free space;
- used space derivabile;
- percentage free derivabile;
- volume identifier quando utile.

Evitare pseudovolumi non rilevanti quando possibile.

---

# 18. GPU / display adapters

Per ogni GPU/adattatore:

- manufacturer;
- model/name;
- driver version;
- driver date, quando disponibile;
- video memory quando affidabile;
- PNP/device identifier se utile.

Questo permetterà anche di controllare in futuro versioni driver o specifiche hardware richieste da software.

---

# 19. Rete

Per ogni adattatore significativo:

- interface name/description;
- adapter type;
- MAC address;
- status;
- link speed;
- DHCP enabled;
- IPv4 addresses;
- IPv6 addresses;
- prefix/subnet;
- default gateway;
- DNS servers;
- connection profile/category, se utile;
- physical vs virtual, quando determinabile.

Dovrà essere possibile distinguere almeno:

- Ethernet;
- Wi-Fi;
- VPN/virtual adapter;
- altri adapter.

Non esportare:

- password Wi-Fi;
- PSK;
- credenziali VPN;
- password;
- secrets.

---

# 20. TPM e sicurezza hardware

Raccogliere quando disponibile:

- presenza TPM;
- TPM ready;
- TPM enabled;
- TPM activated;
- manufacturer;
- manufacturer version;
- specification version;
- eventuale ownership/status non sensibile.

Raccogliere inoltre:

- Secure Boot status;
- eventuali informazioni di Device Encryption/BitLocker utili allo stato.

**Non esportare mai:**

- BitLocker recovery keys;
- TPM secrets;
- certificati privati;
- credenziali;
- password;
- chiavi crittografiche.

---

# 21. BitLocker / cifratura

Se facilmente disponibile, esportare per volume:

- protection status;
- encryption status;
- encryption percentage;
- encryption method;
- volume protetto/non protetto.

Non esportare recovery key o key protector secrets.

---

# 22. Software Win32 classico

L'inventory software non deve basarsi esclusivamente su Winget.

Winget può rimanere una sorgente aggiuntiva, ma il riferimento principale per i programmi installati dovrà includere le chiavi uninstall Windows:

```text
HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*
HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*
HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*
```

Per ogni prodotto raccogliere quando disponibile:

- DisplayName;
- DisplayVersion;
- Publisher;
- InstallDate;
- InstallLocation;
- architecture/source registry;
- install scope (`machine` / `user`);
- registry key identifier;
- product code quando disponibile.

Da valutare quali campi tecnici aggiuntivi abbiano reale valore.

---

# 23. Non utilizzare Win32_Product

Requisito esplicito:

> evitare `Win32_Product`.

Motivazioni:

- lentezza;
- copertura limitata ai pacchetti MSI;
- rischio di avviare consistency check / repair MSI.

---

# 24. AppX / MSIX / Microsoft Store

Aggiungere inventory dei pacchetti AppX/MSIX dove appropriato.

Possibile sorgente:

```powershell
Get-AppxPackage
```

Per ogni pacchetto:

- Name;
- PackageFullName;
- PackageFamilyName;
- Version;
- Publisher;
- Architecture;
- scope/user dove appropriato;
- source/type = AppX/MSIX.

Valutare il filtro dei componenti di sistema per evitare un inventario inutilmente rumoroso.

È preferibile conservare comunque la possibilità di includerli tramite schema/configurazione futura.

---

# 25. Winget

L'attuale elenco Winget può essere mantenuto.

È però opportuno distinguere chiaramente:

```text
software.registry
software.appx
software.winget
```

oppure normalizzare in una singola collection con campo:

```json
"source": "registry"
```

DMT non deve confondere un package individuato da più sorgenti con tre software differenti.

La deduplicazione aggressiva può essere rinviata a DMT.

---

# 26. Utenti locali e utenti rilevati

Raccogliere, quando ragionevolmente disponibile:

- local user accounts;
- enabled/disabled;
- last logon, quando affidabile;
- current interactive user;
- last logged-on user;
- eventuali profili utente locali presenti.

Non utilizzare questi dati come assegnatario amministrativo.

DMT potrà usarli come indizio, ma:

```text
utente rilevato ≠ assegnatario dell'asset
```

Non esportare:

- password;
- password hash;
- credential material.

---

# 27. Dominio / Workgroup / Join state

Raccogliere:

- domain/workgroup;
- domain joined;
- Entra ID/Azure AD joined quando determinabile;
- hybrid join quando determinabile;
- computer domain name;
- eventualmente tenant GUID Entra solo se tecnicamente utile e non problematico.

Non confondere il dominio Windows con il tenant DMT.

---

# 28. Windows Update / patch state

Se disponibile senza introdurre costi di esecuzione eccessivi, raccogliere almeno:

- ultimo aggiornamento cumulativo installato;
- install date;
- KB identifier;
- update history recente rilevante;
- eventuale pending reboot state.

L'obiettivo futuro è poter rispondere a domande come:

> Quali PC non hanno ancora la patch X?

La raccolta non deve però duplicare inutilmente operazioni lente già eseguite dal modulo Microsoft Update.

Preferire il riuso dei dati già ottenuti durante la sessione MT.

---

# 29. Driver

Raccogliere almeno per componenti rilevanti, se sostenibile:

- device/class;
- manufacturer;
- driver provider;
- driver version;
- driver date.

Prima implementazione possibile:

- GPU;
- network;
- storage/chipset principali.

Un dump completo di migliaia di entry PnP va valutato rispetto al costo/rumore.

La struttura JSON deve comunque poter essere estesa in futuro.

---

# 30. Stato della raccolta per sezione

Una sezione non disponibile non deve rendere invalido tutto il JSON.

DMT deve poter distinguere:

```text
nessun TPM presente
```

da:

```text
impossibile interrogare TPM
```

Ogni sezione importante dovrebbe poter riportare uno stato.

Esempio concettuale:

```json
"tpm": {
  "status": "ok",
  "data": {
    "present": true
  }
}
```

oppure:

```json
"tpm": {
  "status": "unavailable",
  "error": "..."
}
```

Valori indicativi:

```text
ok
partial
unavailable
error
not_supported
```

Il dettaglio errore deve essere tecnico ma non contenere segreti.

---

# 31. Error handling

Un errore in una singola categoria di inventory:

- non deve bloccare le altre categorie;
- deve essere registrato;
- deve produrre JSON parziale ma valido;
- deve essere rappresentato nello stato della sezione.

L'inventory completo può quindi avere uno stato generale:

```text
OK
PARTIAL
ERROR
```

`ERROR` dovrebbe essere riservato all'impossibilità di produrre uno snapshot utilizzabile.

---

# 32. Collector metadata

Ogni snapshot deve contenere almeno:

```json
"collector": {
  "name": "Maintenance Toolkit",
  "version": "5.0.0"
}
```

Aggiungere se utile:

- execution/run ID MT;
- elevation state;
- PowerShell version;
- execution architecture;
- language;
- collection duration.

Esempio:

```json
"collection": {
  "startedAt": "...",
  "completedAt": "...",
  "durationMs": 1320,
  "status": "ok"
}
```

Il dato `durationMs` sarà molto utile per i benchmark.

---

# 33. Dati che MT non deve gestire

Non inserire nel modello logico di MT dati quali:

- tenant DMT;
- site/sede;
- dipartimento;
- assegnatario ufficiale;
- funzione/destinazione d'uso;
- data acquisto;
- fornitore;
- costo;
- fattura;
- contratto;
- leasing/noleggio;
- scadenza contratto;
- garanzia amministrativa;
- estensioni di garanzia;
- data dismissione;
- stato asset DMT.

Questi dati appartengono a DMT.

---

# 34. Sicurezza / privacy

Principio:

> inventory tecnico sì, raccolta di segreti no.

Escludere esplicitamente:

- password;
- Wi-Fi keys;
- VPN secrets;
- browser passwords;
- credential manager secrets;
- BitLocker recovery keys;
- private keys;
- token di autenticazione;
- contenuti dei file utente;
- cronologie browser;
- contenuti email;
- dati personali non necessari all'inventory tecnico.

---

# 35. Compatibilità e standalone

MT 5.0 deve poter continuare a essere utilizzato:

- da chiavetta;
- localmente;
- manualmente;
- senza dominio;
- senza DMT;
- senza share;
- senza database;
- senza connessione a infrastrutture DMT.

Il nuovo export remoto deve essere completamente opzionale.

---

# 36. Compatibilità con GPO / esecuzione centralizzata

Il nuovo parametro CLI deve essere:

- non interattivo;
- facilmente impostabile in uno script/GPO;
- compatibile con UNC path;
- quotabile correttamente;
- indipendente dalla lingua UI.

Esempio:

```text
\\SERVER\DMT\ZENIT\SAN-CESARIO\incoming
```

La GPO potrà quindi scegliere una destinazione diversa per OU/sede.

---

# 37. Possibile gestione di più destinazioni

Non è requisito obbligatorio per la prima MT 5.0.

Tuttavia si suggerisce di evitare una struttura che renda impossibile in futuro:

```powershell
-InventoryShare path1,path2
```

oppure destinazioni multiple configurabili.

Per MT 5.0 è sufficiente **una destinazione remota opzionale**.

---

# 38. JSON raw e forward compatibility

Preferire:

- proprietà semanticamente chiare;
- array anche quando oggi esiste tipicamente un solo componente, se la cardinalità può essere multipla;
- tipi coerenti;
- numeri numerici, non stringhe formattate;
- bytes o valori tecnici raw dove possibile;
- unità esplicitate nello schema.

Evitare:

```json
"ram": "32 GB"
```

Preferire:

```json
"ramBytes": 34359738368
```

DMT potrà visualizzare:

```text
32 GB
```

senza perdere precisione.

Stesso principio per:

- disk size;
- free space;
- speed;
- timestamps;
- percentages.

---

# 39. Null, assente e unknown

Definire una convenzione chiara.

Indicativamente:

- proprietà assente = non prevista/non raccolta dallo schema/collector;
- `null` = prevista ma valore non disponibile;
- status `unavailable/error` = raccolta tentata ma non riuscita.

Evitare stringhe ambigue come:

```text
"N/A"
"Unknown"
"-"
```

nei valori strutturati.

---

# 40. Encoding

JSON:

```text
UTF-8
```

preferibilmente senza dipendenze dalla code page locale.

Devono essere gestiti correttamente:

- nomi software internazionali;
- manufacturer;
- username;
- caratteri non ASCII.

---

# 41. Benchmark MT 4.0 vs MT 5.0

Prima di considerare conclusa la feature, effettuare test comparativi reali.

L'obiettivo non è fissare a priori un limite artificiale, ma verificare che la nuova raccolta dati non trasformi una fase rapida in un collo di bottiglia.

## Baseline

Misurare MT 4.0 sullo stesso PC con:

- inventory attuale;
- tempo totale MT quando utile;
- CPU;
- RAM;
- eventuali attese Winget.

## Candidate

Misurare MT 5.0 sullo stesso hardware e nelle stesse condizioni.

Raccogliere almeno:

```text
Inventory collection duration
JSON serialization duration
Local write duration
Remote copy duration
JSON file size
Peak/indicative process memory
Total MT duration
```

Separare la raccolta tecnica dalla copia su rete.

---

# 42. Benchmark per sezione

È fortemente consigliato introdurre profiling per categorie:

```text
Identity          40 ms
OS                55 ms
Firmware          80 ms
CPU               20 ms
RAM               45 ms
Storage          170 ms
Network           90 ms
GPU               35 ms
TPM              120 ms
Software        1450 ms
AppX             620 ms
Users             90 ms
Updates          ...
```

Questo consentirà di sapere immediatamente quale query pesa realmente.

MT dispone già di componenti di profiling nella propria architettura 4.0; verificare se possano essere riutilizzati.

---

# 43. Criterio di sostenibilità

Non fissare ancora una soglia definitiva prima dei test reali.

Principio:

> il valore dell'inventory esteso deve essere confrontato con il costo reale misurato.

Se una categoria produce un aumento sproporzionato del tempo di esecuzione, valutarne:

- ottimizzazione;
- riuso di dati già ottenuti da altri moduli;
- raccolta condizionata;
- caching;
- spostamento a fase successiva.

Non eliminare preventivamente dati utili senza benchmark.

---

# 44. Test minimo su hardware differente

Effettuare se possibile almeno:

- PC moderno Windows 11;
- PC meno recente;
- notebook;
- macchina con/without TPM;
- macchina con più dischi;
- macchina senza Winget o con Winget problematico;
- computer domain joined;
- computer standalone.

Obiettivo: performance e robustezza, non solo correttezza sul PC di sviluppo.

---

# 45. Test della share

Verificare almeno:

## Share disponibile

```text
local JSON       OK
remote JSON      OK
MT               OK
```

## Share irraggiungibile

```text
local JSON       OK
remote JSON      WARN/FAIL COPY
MT               CONTINUA
```

## Permessi negati

Come sopra.

## Connessione lenta

La copia di un JSON di piccole dimensioni non deve influire significativamente, ma impostare comunque timeout/comportamenti ragionevoli dove applicabile.

---

# 46. Test di atomicità

Simulare/interrompere:

- scrittura;
- copia;
- rename.

Verificare che DMT non possa trovare un `.json` finale parzialmente scritto.

---

# 47. Test duplicazione

Ogni esecuzione deve generare un `snapshotId` diverso.

La ripetizione/copia dello stesso file deve conservare lo stesso snapshotId.

La deduplicazione sarà responsabilità di DMT.

---

# 48. Compatibilità futura DMT

DMT è progettata per:

- polling configurabile della share;
- import manuale;
- cartelle associate a tenant/site;
- directory `incoming`;
- archiviazione `processed`;
- gestione `rejected`;
- deduplicazione per snapshotId;
- asset identity separata dall'hostname;
- storico hardware/software;
- multi-tenant futuro.

MT non deve implementare queste funzioni.

Deve soltanto produrre snapshot affidabili.

---

# 49. Priorità di implementazione proposta

## P0 — indispensabile MT 5.0

1. Inventory Schema 1.0.
2. snapshotId.
3. timestamp ISO 8601.
4. collector/version metadata.
5. salvataggio JSON locale sempre.
6. parametro CLI destinazione remota opzionale.
7. scrittura `.tmp` + rename.
8. identificatori macchina multipli.
9. OS.
10. BIOS/UEFI.
11. CPU.
12. RAM totale + DIMM.
13. storage fisico + volumi.
14. GPU.
15. rete.
16. TPM/Secure Boot.
17. software Win32 registry.
18. AppX/MSIX.
19. utenti rilevati/local users.
20. domain/join state.
21. error status per sezione.
22. nessun segreto.
23. benchmark MT4/MT5.

## P1 — fortemente desiderabile

- BitLocker state senza recovery key;
- driver principali/versioni;
- Windows patch/update state;
- firmware disco;
- device/chassis metadata aggiuntivi;
- profiling dettagliato per sezione.

## P2 — valutabile dopo benchmark

- inventory PnP completo;
- cronologia update molto estesa;
- informazioni hardware specialistiche;
- sorgenti software aggiuntive;
- destinazioni remote multiple.

---

# 50. Deliverable richiesti alla chat/sviluppo MT 5.0

Prima dell'implementazione definitiva:

1. proposta Inventory Schema v1 completa;
2. elenco delle API/CIM/registry source utilizzate;
3. valutazione di eventuali campi problematici/non affidabili;
4. proposta del nome CLI definitivo;
5. strategia di scrittura locale/remota;
6. implementazione incrementale;
7. JSON di esempio reale anonimizzato;
8. test automatici dove praticabili;
9. benchmark comparativo MT 4.0 / MT 5.0;
10. documentazione tecnica del formato.

---

# 51. Acceptance criteria funzionali

La feature può considerarsi completata quando:

- MT funziona senza DMT;
- ogni esecuzione produce un JSON locale valido;
- ogni snapshot ha GUID univoco;
- il JSON contiene schemaVersion;
- il JSON contiene collectorVersion;
- l'hostname non viene usato come unico identificatore;
- con CLI remota viene prodotto anche il file sulla share;
- share offline non interrompe MT;
- file remoti incompleti non diventano `.json`;
- una sezione fallita non invalida tutte le altre;
- il software inventory non usa `Win32_Product`;
- non vengono esportati segreti;
- DMT può utilizzare il JSON senza conoscere internals di MT;
- esiste un benchmark MT 4.0 vs MT 5.0 documentato.

---

# 52. Principio finale

Maintenance Toolkit 5.0 non deve diventare un agente RMM.

Deve restare:

> **Maintenance Toolkit + technical inventory snapshot producer**

DMT sarà responsabile di:

- asset management;
- correlazione;
- tenancy;
- sites;
- departments;
- assignments;
- lifecycle;
- warranty/contracts;
- storico;
- query;
- dashboard;
- alert;
- reporting.

La separazione tra i due prodotti deve rimanere netta.

---

# 53. Nota sulle performance

L'obiettivo condiviso è raccogliere **tutti i dati tecnici realmente utili**.

Non verranno esclusi campi soltanto per timore teorico di rallentamenti.

La procedura corretta sarà:

```text
implementare
     ↓
misurare
     ↓
confrontare MT 4 / MT 5
     ↓
ottimizzare le sezioni realmente costose
```

Il benchmark reale sarà quindi parte integrante dello sviluppo MT 5.0 e non un controllo opzionale successivo.
