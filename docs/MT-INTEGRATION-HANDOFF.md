# HANDOFF — Maintenance Toolkit → DMT Inventory Export

**Data:** 2026-08-30  
**Progetto:** Maintenance Toolkit / DMT  
**Scopo:** passaggio di consegne alla chat di sviluppo di Maintenance Toolkit per implementare l'esportazione dell'inventario macchina destinato a DMT (Dashboard Maintenance Toolkit).

---

## 1. Contesto

È stato deciso di sviluppare **DMT (Dashboard Maintenance Toolkit)** come componente separato da Maintenance Toolkit, ma appartenente allo stesso ecosistema/progetto.

Maintenance Toolkit e DMT devono poter funzionare anche indipendentemente:

- **Maintenance Toolkit (MT)** resta il componente locale eseguito sugli endpoint Windows.
- **DMT** sarà il componente di raccolta, indicizzazione, interrogazione e visualizzazione centralizzata.
- MT deve produrre un formato dati stabile e documentato che DMT possa importare.
- La dashboard non deve dipendere direttamente dal codice o dalla versione interna di MT: deve dipendere dal **contratto dati**.

L'obiettivo funzionale è creare un sistema di inventory management leggero per sysadmin, PMI e small business, con alcune funzionalità concettualmente simili alla parte di inventory di Spiceworks/RMM, senza trasformare MT in un RMM completo.

---

## 2. Decisioni architetturali già prese

### Repository

DMT avrà un **repository GitHub separato** da Maintenance Toolkit.

I due repository saranno comunque parte dello stesso ecosistema.

Naming indicativo:

- `maintenance-toolkit`
- `maintenance-toolkit-dashboard`

Il nome definitivo del repository DMT può essere deciso successivamente.

---

## 3. Ruolo di Maintenance Toolkit

MT deve diventare il **primo producer ufficiale dell'Inventory Schema**.

Durante ogni esecuzione programmata o manuale, MT dovrà:

1. raccogliere informazioni sull'endpoint;
2. raccogliere l'elenco dei software installati e le relative versioni;
3. normalizzare i dati;
4. produrre un file JSON strutturato;
5. salvare il JSON localmente;
6. quando configurato, aggiornare una posizione condivisa/centrale con il JSON più recente della macchina.

La produzione dell'inventario deve essere una feature di MT, ma il formato deve essere abbastanza indipendente da consentire in futuro altri producer, ad esempio:

- collector standalone;
- script PowerShell indipendente;
- import da CSV;
- altri strumenti.

---

## 4. Formato principale: JSON

È stato deciso di usare **JSON come formato canonico di scambio**.

CSV potrà eventualmente essere supportato come formato di export/reportistica, ma **non deve essere il formato master**.

Motivi:

- struttura gerarchica;
- facile estensibilità;
- leggibilità;
- facile importazione;
- possibilità di aggiungere sezioni senza rompere i consumer;
- adatto a futura API;
- semplice da archiviare come snapshot raw.

---

## 5. Versionamento dello schema

Il JSON deve contenere esplicitamente una versione dello schema.

Esempio:

```json
{
  "schemaVersion": "1.0"
}
```

La dashboard dovrà basarsi sullo schema e non sulla specifica versione di MT.

È importante fissare subito questo concetto per evitare dipendenze rigide tra MT e DMT.

---

## 6. Struttura preliminare dell'Inventory Schema v1

La seguente struttura è una **base di lavoro**, non necessariamente lo schema definitivo.

```json
{
  "schemaVersion": "1.0",
  "collectedAt": "2026-08-30T08:00:00+02:00",
  "collector": {
    "name": "Maintenance Toolkit",
    "version": "4.x.x"
  },
  "device": {
    "hostname": "PC-001",
    "manufacturer": "Dell Inc.",
    "model": "Latitude 5540",
    "serialNumber": "ABC1234",
    "cpu": "Intel Core i7-1365U",
    "ramGB": 32
  },
  "os": {
    "name": "Microsoft Windows 11 Pro",
    "version": "24H2",
    "build": "26100"
  },
  "software": [
    {
      "name": "7-Zip 24.09 (x64 edition)",
      "version": "24.09",
      "publisher": "Igor Pavlov",
      "installDate": null
    }
  ]
}
```

---

## 7. Informazioni minime da raccogliere

### Identità macchina

Minimo richiesto:

- hostname;
- serial number;
- manufacturer;
- model.

Da valutare come campi aggiuntivi:

- UUID hardware / SMBIOS UUID;
- dominio / workgroup;
- username principale o ultimo utente;
- eventuale asset tag OEM.

**Nota importante:** il vero identificatore persistente della macchina non dovrebbe essere solamente l'hostname, perché può cambiare o essere duplicato in ambienti diversi.

Va quindi valutata una strategia per un `deviceId` stabile.

---

## 8. Hardware

Informazioni inizialmente desiderate:

- CPU;
- RAM totale;
- manufacturer;
- model;
- serial number.

Da considerare in una fase successiva:

- dischi fisici;
- capacità;
- spazio libero;
- tipo SSD/HDD/NVMe;
- stato SMART, se affidabile e disponibile;
- GPU;
- schede di rete;
- MAC address;
- BIOS/UEFI version;
- BIOS date.

Non è necessario implementare tutto immediatamente: lo schema deve però consentire l'estensione futura.

---

## 9. Sistema operativo

Informazioni desiderate:

- nome sistema operativo;
- edition;
- versione;
- release Windows (es. 24H2);
- build;
- architecture;
- install date, se affidabile;
- ultimo boot / uptime, se utile.

Esempio concettuale:

```json
"os": {
  "name": "Microsoft Windows 11 Pro",
  "version": "24H2",
  "build": "26100",
  "architecture": "x64"
}
```

---

## 10. Inventario software

Per i software Win32/classici, evitare `Win32_Product`.

### Motivo

`Win32_Product`:

- è lento;
- interroga solo prodotti MSI;
- può provocare consistency check / repair degli installer MSI.

### Metodo consigliato

Leggere le chiavi uninstall del registro:

```powershell
$paths = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)

Get-ItemProperty $paths -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName } |
    Select-Object DisplayName, DisplayVersion, Publisher, InstallDate
```

Il campo `DisplayVersion` rappresenta la versione dichiarata dal software nel registro.

### Dati software minimi

Per ogni software:

- name / DisplayName;
- version / DisplayVersion;
- publisher;
- installDate, se presente.

Da valutare anche:

- architecture;
- installLocation;
- uninstallString;
- scope (`machine` / `user`);
- source (`registry`, `appx`, ecc.).

---

## 11. Applicazioni Microsoft Store / MSIX / AppX

Le applicazioni Store non sono necessariamente rappresentate nelle normali chiavi uninstall.

Andrà quindi valutata l'integrazione di:

```powershell
Get-AppxPackage
```

Lo schema dovrebbe consentire di distinguere almeno la sorgente/tipologia del pacchetto.

Esempio futuro:

```json
{
  "name": "Example App",
  "version": "1.2.3.4",
  "publisher": "Example Corp.",
  "source": "appx"
}
```

---

## 12. Normalizzazione e duplicati

Il collector dovrà gestire possibili duplicati dovuti a:

- chiavi 32 bit / 64 bit;
- installazioni per-machine e per-user;
- prodotti registrati più volte;
- software con nomi leggermente differenti.

Non eliminare aggressivamente informazioni in questa prima fase.

Meglio mantenere i dati originari utili e definire una normalizzazione chiara, perché DMT potrebbe in futuro effettuare una seconda normalizzazione lato importer/database.

---

## 13. Posizione dei JSON

Obiettivo operativo:

ogni endpoint produce il proprio inventario e lo aggiorna su una share configurata.

Esempio concettuale:

```text
\\SERVER\MT-Inventory\
    PC001.json
    PC002.json
    SERVER01.json
```

È preferibile che ogni endpoint aggiorni **solo il proprio file**.

Questo riduce:

- collisioni;
- problemi di locking;
- complessità lato MT.

---

## 14. Scrittura sicura del file

Da considerare nella implementazione MT:

1. generare il JSON in un file temporaneo;
2. completare la scrittura;
3. sostituire atomicamente, per quanto possibile, il JSON definitivo.

Obiettivo: evitare che DMT legga un file parzialmente scritto.

Esempio concettuale:

```text
PC001.json.tmp
       ↓
PC001.json
```

---

## 15. Cronologia

È emersa la necessità futura di poter rispondere non solo a:

> Cosa è installato ora?

ma anche:

> Cosa è cambiato dall'ultima esecuzione?

Per questo motivo è utile distinguere:

### Snapshot corrente

```text
current/PC001.json
```

### Eventuale storico

```text
history/PC001/20260830T080000.json
```

**Non è obbligatorio implementare subito lo storico lato MT.**

È possibile che sia DMT, durante l'importazione, a storicizzare i cambiamenti.

Va però evitata una scelta architetturale che renda impossibile farlo successivamente.

---

## 16. Storage futuro di DMT

Decisione preliminare discussa:

### Fase iniziale

- JSON come fonte raw;
- SQLite come database locale/centrale della dashboard.

### Possibile evoluzione

- PostgreSQL per installazioni multiutente, multisede o MSP;
- eventualmente uso di JSONB per conservare anche il payload raw.

Questa decisione riguarda soprattutto DMT, ma implica che MT deve produrre JSON facilmente importabile e stabile.

---

## 17. Domande a cui DMT dovrà poter rispondere

L'inventario raccolto da MT deve permettere, in prospettiva, query come:

- Quali PC hanno ancora Windows 10?
- Quali PC hanno Office 2016?
- Dove è installato un determinato software?
- Quale versione di Chrome è installata sui vari endpoint?
- Quali macchine hanno una versione software inferiore a una certa release?
- Quali PC hanno meno di 8/16 GB di RAM?
- Quali macchine non effettuano un check-in da N giorni?
- Su quali macchine è installato Java 8?
- Dove è presente AnyDesk?
- Quali endpoint hanno modificato il proprio inventario dall'ultima esecuzione?

Queste query sono il criterio pratico con cui valutare quali informazioni devono essere esportate.

---

## 18. Last Seen / Check-in

Ogni JSON deve contenere almeno un timestamp affidabile:

```json
"collectedAt": "2026-08-30T08:00:00+02:00"
```

Questo consentirà a DMT di determinare:

- ultimo inventario ricevuto;
- endpoint non più attivi;
- macchine che non eseguono MT da troppo tempo.

Preferire timestamp ISO 8601.

---

## 19. Informazioni sul collector

Il payload dovrebbe indicare chi ha prodotto il file.

Esempio:

```json
"collector": {
  "name": "Maintenance Toolkit",
  "version": "4.0.0"
}
```

Questo servirà per:

- troubleshooting;
- compatibilità;
- migrazioni schema;
- individuazione di endpoint con versioni MT obsolete.

---

## 20. Indipendenza tra MT e DMT

Principio fondamentale:

> MT produce Inventory Schema.  
> DMT consuma Inventory Schema.

Non:

> DMT legge strutture interne specifiche di MT.

Questo permetterà in futuro:

```text
Maintenance Toolkit ──┐
                      │
Collector standalone ─┼──> Inventory Schema ──> DMT
                      │
CSV importer ─────────┘
```

---

## 21. Cosa NON stiamo costruendo, almeno per ora

DMT/MT non deve essere progettato come un RMM completo.

Non sono obiettivi correnti:

- remote shell;
- remote desktop;
- gestione centralizzata delle credenziali;
- distribuzione software generalizzata;
- controllo remoto completo;
- patch management enterprise;
- agent always-on dedicato.

Il focus iniziale è:

- Maintenance;
- Inventory;
- Visibility;
- History;
- Search / Query;
- Dashboard.

---

## 22. Prossimo lavoro richiesto alla chat Maintenance Toolkit

Partendo da questo handoff, la chat MT dovrà progettare e implementare la feature di inventory export.

Ordine consigliato:

1. verificare l'architettura attuale di MT 4.x;
2. individuare il punto corretto del workflow in cui raccogliere l'inventario;
3. definire `Inventory Schema v1`;
4. definire un identificatore persistente della macchina;
5. implementare raccolta device/hardware;
6. implementare raccolta OS;
7. implementare inventario software Win32 dal registro;
8. valutare AppX/MSIX;
9. serializzare in JSON;
10. implementare salvataggio locale;
11. implementare opzionalmente copia/aggiornamento su share configurabile;
12. documentare configurazione e formato JSON;
13. aggiungere logging/error handling senza interrompere il normale funzionamento di MT se la share non è raggiungibile.

---

## 23. Requisito importante di resilienza

La funzione DMT inventory **non deve rendere MT dipendente dalla disponibilità della share o della dashboard**.

Se:

- la share non è raggiungibile;
- il server DMT è offline;
- l'upload/copia fallisce;

MT deve continuare normalmente.

L'inventario remoto è una funzione aggiuntiva, non un requisito per completare la manutenzione locale.

Idealmente:

```text
raccolta locale -> sempre
scrittura JSON locale -> sempre, se possibile
pubblicazione remota -> best effort + log
```

---

## 24. Stato del progetto DMT

Questa conversazione è stata definita come **chat ufficiale di DMT**.

La chat DMT continuerà a progettare:

- schema logico;
- importer;
- SQLite;
- futura compatibilità PostgreSQL;
- dashboard;
- query;
- storico;
- eventuale modello multi-cliente/MSP;
- UI/UX.

La chat Maintenance Toolkit deve invece concentrarsi sul ruolo di **producer/collector**.

---

## 25. Principio guida

La feature deve essere utile già da sola, semplice da installare e coerente con la filosofia di Maintenance Toolkit.

L'obiettivo non è creare immediatamente un'infrastruttura complessa, ma costruire una base affidabile che consenta nel tempo di arrivare a uno strumento importante per sysadmin di PMI e small business.

