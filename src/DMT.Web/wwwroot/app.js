'use strict';
const $ = id => document.getElementById(id);
let strings = {}, locale = 'en-US', view = 'computers', systems = [], products = [], selected = null, softwareRows = [];
let maintenanceCases = [];
let softwareQuery = '';
let softwareReady = false;
let generation = 0, detailGeneration = 0, startedAt = 0, loadingTimer, page = 0;
const t = key => { if (strings[key] === undefined) { console.warn('missing_translation', key); return `[MISSING: ${key}]`; } return strings[key]; };
const format = value => new Intl.NumberFormat(locale).format(value);
const date = value => value ? new Intl.DateTimeFormat(locale, {dateStyle:'medium', timeStyle:'short'}).format(new Date(value)) : t('web.unknown');
async function get(url) { const response = await fetch(url, {cache:'no-store'}); if (response.status === 401) location.assign('/login'); if (!response.ok) throw new Error(`http_${response.status}`); return response.json(); }
function text(tag, value, className) { const element = document.createElement(tag); element.textContent = value ?? t('web.unknown'); if (className) element.className = className; return element; }
function reportMessage(value, className) {
  const result=DmtReportText.display(value), container=document.createElement('div');
  container.className='report-message';
  container.append(text('p',result.text,className));
  if (result.corrected) {
    container.append(text('p',t('web.reportEncodingNotice'),'encoding-note'));
    const original=document.createElement('details'); original.className='report-original';
    original.append(text('summary',t('web.reportOriginal')),text('p',result.original,'review-evidence'));
    container.append(original);
  }
  return container;
}
function state(value, kind = 'inventory') {
  const known = ['ok','partial','error'].includes(value);
  const key = kind === 'maintenance' && value === 'ok' ? 'web.maintenanceOk' : `web.status.${known ? value : 'unknown'}`;
  const element = text('span', t(key), `status ${known ? value : 'unknown'}`);
  if (!known && value) element.title = value;
  return element;
}
function translate() {
  document.documentElement.lang = locale; document.title = t('app.name');
  for (const element of document.querySelectorAll('[data-i18n]')) element.textContent = t(element.dataset.i18n);
  for (const element of document.querySelectorAll('[data-i18n-placeholder]')) element.placeholder = t(element.dataset.i18nPlaceholder);
  for (const element of document.querySelectorAll('[data-i18n-title]')) element.title = t(element.dataset.i18nTitle);
  $('badge').setAttribute('aria-label', t('web.badge'));
  $('view-title').textContent = t(view === 'maintenance' ? 'web.maintenanceReview' : view === 'computers' ? 'web.computers' : 'nav.software');
}
function feedback(key, error = false) {
  clearInterval(loadingTimer); $('feedback').replaceChildren();
  if (!key) return;
  const node = text('div', t(key), error ? 'error-box' : 'loading'); $('feedback').append(node);
  if (!error) { startedAt = performance.now(); loadingTimer = setInterval(() => { node.textContent = `${t(key)} · ${format(Math.floor((performance.now()-startedAt)/1000))} ${t('web.seconds')}`; }, 1000); }
}
function params() {
  const query = new URLSearchParams();
  if (selected) query.set('asset', selected.asset_id);
  for (const [id, key] of [['name-query','name'],['publisher-query','publisher'],['version-query','version']])
    if ($(id).value.trim()) query.set(key, $(id).value.trim());
  return query.toString();
}
function heading(keys) { const row = document.createElement('tr'); for (const key of keys) { const th = text('th', t(key)); th.scope = 'col'; row.append(th); } $('table-head').replaceChildren(row); }
function cell(row, value, secondary) { const td = document.createElement('td'); if (value instanceof Node) td.append(value); else td.append(text('span', value)); if (secondary) td.append(text('span', secondary, 'cell-secondary')); row.append(td); return td; }
function computerButton(system) { const button = text('button', system.hostname, 'link-button'); button.type = 'button'; button.addEventListener('click', () => openDetail(system)); return button; }
function render() {
  translate(); $('nav-computers').classList.toggle('active', view === 'computers'); $('nav-software').classList.toggle('active', view === 'software');
  for (const id of ['computer-filter','os-filter']) $(id).hidden = view === 'software';
  for (const id of ['name-filter','publisher-filter','version-filter']) $(id).hidden = view !== 'software';
  $('maintenance-filter').hidden = view !== 'maintenance';
  $('exports').hidden = view !== 'software' || !softwareReady;
  $('export').href = '/api/software.csv?' + softwareQuery;
  $('export-computers').href = '/api/software-computers.txt?' + softwareQuery;
  $('export-versions').href = '/api/software-computers.csv?' + softwareQuery;
  $('selection').hidden = view !== 'software' || !selected;
  if (selected) $('selection-text').textContent = `${t('web.computer')}: ${selected.hostname} · ${t('web.softwareStatus')}: ${t('web.status.' + (['ok','partial','error'].includes(selected.software_status) ? selected.software_status : 'unknown'))}`;
  $('computer-count').textContent = format(systems.length); $('product-count').textContent = format(products.length);
  $('attention-count').textContent = format(maintenanceCases.length);
  $('maintenance-card').setAttribute('aria-pressed', String(view === 'maintenance'));
  $('clear-maintenance').hidden = view !== 'maintenance';
  let rows;
  if (view !== 'software') {
    const query = $('computer-query').value.trim().toLocaleLowerCase(locale), os = $('os-query').value.trim().toLocaleLowerCase(locale);
    const kind = $('maintenance-kind').value;
    const attention = new Set(maintenanceCases.filter(m => kind === 'all' || m.findings.some(f => f.kind === kind && f.updateCandidate)).map(m => m.assetId));
    rows = systems.filter(s => (view !== 'maintenance' || attention.has(s.asset_id)) && (!query || [s.hostname,s.serial].some(v => String(v ?? '').toLocaleLowerCase(locale).includes(query))) && (!os || String(s.os_name ?? '').toLocaleLowerCase(locale).includes(os)));
    heading(view === 'maintenance' ? ['web.computer','web.lastSeen','web.review.issues','web.maintenance'] : ['web.computer','web.os','web.lastSeen','web.softwareStatus','web.maintenance']);
  } else { rows = softwareRows; heading(['web.productName','web.publisher','web.version','web.computer','web.softwareStatus']); }
  const pages = Math.max(1, Math.ceil(rows.length / 100)); page = Math.min(page, pages - 1);
  $('paging').hidden = pages === 1; $('previous-page').disabled = page === 0; $('next-page').disabled = page === pages - 1;
  $('page-count').textContent = `${format(page + 1)} / ${format(pages)}`;
  const body = document.createDocumentFragment();
  for (const item of rows.slice(page * 100, (page + 1) * 100)) {
    const row = document.createElement('tr');
    if (view === 'maintenance') {
      const review = maintenanceCases.find(m => m.assetId === item.asset_id);
      cell(row, computerButton(item), item.serial); cell(row, date(item.collected_at));
      const kind=$('maintenance-kind').value;
      const displayed=review.findings.filter(f => kind === 'all' || f.kind === kind && f.updateCandidate);
      cell(row, format(displayed.length), displayed.map(f => t('web.review.kind.' + f.kind) + (f.installedVersion || f.targetVersion ? ': ' + (f.installedVersion ?? t('web.unknown')) + ' → ' + (f.targetVersion ?? t('web.unknown')) : '')).join(' · '));
      cell(row, state(item.maintenance_status, 'maintenance'));
    } else if (view === 'computers') {
      cell(row, computerButton(item), item.serial);
      cell(row, item.os_name, [item.os_version,item.os_build].filter(Boolean).join(' · '));
      cell(row, date(item.collected_at)); cell(row, state(item.software_status)); cell(row, state(item.maintenance_status, 'maintenance'));
    } else {
      cell(row, item.name, item.source); cell(row, item.publisher); cell(row, item.version);
      const system = systems.find(s => s.asset_id === item.asset_id); cell(row, system ? computerButton(system) : item.hostname);
      cell(row, state(item.software_status));
    }
    body.append(row);
  }
  $('table-body').replaceChildren(body); $('empty').hidden = rows.length > 0;
  $('result-count').textContent = `${t('web.results')}: ${format(rows.length)}`;
}
async function load() {
  const request = ++generation; feedback('web.loading');
  softwareReady = false; $('exports').hidden = true;
  const requestedSoftwareQuery = params();
  try {
    const [newSystems, newProducts, newSoftware, newMaintenance] = await Promise.all([get('/api/systems'), get('/api/products'), view === 'software' ? get('/api/software?' + requestedSoftwareQuery) : Promise.resolve([]), get('/api/maintenance')]);
    if (request !== generation) return;
    systems = newSystems; products = newProducts; softwareRows = newSoftware; maintenanceCases = newMaintenance; page = 0;
    softwareQuery = requestedSoftwareQuery;
    softwareReady = view === 'software';
    if (selected) selected = systems.find(s => s.asset_id === selected.asset_id) ?? null;
    render(); feedback(null);
  } catch (error) { if (request === generation) { feedback('web.loadError', true); console.error(error); } }
}
async function openDetail(system) {
  const request = ++detailGeneration;
  $('detail').hidden = false; $('detail-title').textContent = system.hostname;
  $('detail-content').replaceChildren(text('p', t('web.loading'), 'muted'));
  $('detail').scrollIntoView({behavior:'auto',block:'nearest'});
  try {
    const [history, review] = await Promise.all([get(`/api/assets/${encodeURIComponent(system.asset_id)}/history`), get(`/api/assets/${encodeURIComponent(system.asset_id)}/maintenance`)]);
    if (request !== detailGeneration) return;
    const content = document.createDocumentFragment(), grid = document.createElement('div'); grid.className = 'detail-grid';
    for (const [key,value] of [['web.os',system.os_name],['web.serial',system.serial],['web.lastSeen',date(system.collected_at)],['web.osStatus',state(system.os_status)],['web.softwareStatus',state(system.software_status)],['web.maintenance',state(system.maintenance_status, 'maintenance')]]) {
      const dl = document.createElement('dl'); dl.append(text('dt', t(key))); const dd = document.createElement('dd'); if (value instanceof Node) dd.append(value); else dd.textContent = value ?? t('web.unknown'); dl.append(dd); grid.append(dl);
    }
    content.append(grid);
    const reviewPanel = document.createElement('section'); reviewPanel.className = 'review-panel';
    reviewPanel.append(text('h3', t('web.review.issues')), text('p', t('web.review.observation') + ' ' + date(review.collectedAt), 'muted'));
    if (review.findings.length === 0) reviewPanel.append(text('p', t('web.review.none'), 'muted'));
    if (review.findings.some(f => f.updateCandidate)) reviewPanel.append(text('p', t('web.review.remoteUnavailable'), 'review-notice'));
    for (const finding of [...review.findings].sort((a,b) => Number(b.updateCandidate) - Number(a.updateCandidate))) {
      const card = document.createElement('article'); card.className='review-finding';
      card.append(text('h4', t('web.review.kind.' + finding.kind)));
      const statusKey = 'web.review.status.' + finding.status;
      card.append(text('span', strings[statusKey] ?? finding.status, 'status ' + (finding.status === 'error' || finding.status === 'failed' || finding.status === 'verification_failed' ? 'error' : 'partial')));
      if (finding.name) card.append(reportMessage(finding.name,'review-name'));
      if (finding.detail) card.append(reportMessage(finding.detail,'review-evidence'));
      if (finding.installedVersion || finding.targetVersion) {
        const versions=document.createElement('div'); versions.className='review-versions';
        versions.append(text('span',t('web.review.installed')+': '+(finding.installedVersion ?? t('web.unknown'))), text('span',t('web.review.candidate')+': '+(finding.targetVersion ?? t('web.unknown')))); card.append(versions);
      }
      if (finding.rebootRequired === true) card.append(text('p',t('web.review.rebootPending'),'review-notice'));
      if (finding.updateCandidate) {
        const update=text('button',t('web.review.updateNow'),'button primary'); update.type='button'; update.disabled=true; update.title=t('web.review.remoteUnavailable');
        card.append(update,text('p',t('web.review.noRemoteReboot'),'muted'));
      }
      reviewPanel.append(card);
    }
    content.append(reviewPanel);
    const button = text('button', t('web.showSoftware'), 'button primary'); button.type='button'; button.addEventListener('click', () => { selected = system; view='software'; $('detail').hidden=true; $('name-query').value=''; $('publisher-query').value=''; $('version-query').value=''; render(); load(); }); content.append(button);
    content.append(text('h3', t('web.history')));
    for (const snapshot of history) {
      const row = document.createElement('div'); row.className='history-row'; row.append(text('span', date(snapshot.collected_at)));
      const status = document.createElement('span'); status.append(text('span', t('web.maintenance') + ': '), state(snapshot.maintenance_status, 'maintenance')); row.append(status); content.append(row);
      const sections = JSON.parse(snapshot.sections_json);
      for (const [section, data] of Object.entries(sections))
        for (const error of Array.isArray(data.errors) ? data.errors : [])
          content.append(reportMessage([section, error.source, error.code].filter(Boolean).join(': '), 'cell-secondary'));
    }
    $('detail-content').replaceChildren(content);
    $('detail').scrollIntoView({behavior:'auto',block:'start'});
  } catch (error) { if (request === detailGeneration) $('detail-content').replaceChildren(text('p',t('web.loadError'),'error-box')); }
}
async function chooseLanguage(code) {
  const pack = await get('/api/languages/' + encodeURIComponent(code)); strings = pack.strings; locale = pack.code;
  $('language').value = locale;
  try { localStorage.setItem('dmt.language',locale); } catch {}
  detailGeneration++; $('detail').hidden=true; render();
}
$('nav-computers').addEventListener('click', () => { view='computers'; generation++; render(); load(); });
$('maintenance-card').addEventListener('click', () => { view='maintenance'; selected=null; page=0; $('computer-query').value=''; $('os-query').value=''; detailGeneration++; $('detail').hidden=true; render(); load(); });
$('clear-maintenance').addEventListener('click', () => { view='computers'; page=0; render(); });
$('maintenance-kind').addEventListener('change', () => { page=0; detailGeneration++; $('detail').hidden=true; render(); });
$('nav-software').addEventListener('click', () => { view='software'; selected=null; render(); load(); });
$('refresh').addEventListener('click', load);
$('logout').addEventListener('click', async () => {
  const token=await get('/auth/token');
  const response=await fetch('/auth/logout',{method:'POST',headers:{'X-DMT-CSRF':token.token}});
  if(response.ok)location.assign('/login');
});
$('filters').addEventListener('submit', event => { event.preventDefault(); view!=='software' ? render() : load(); });
for (const id of ['computer-query','os-query']) $(id).addEventListener('input', () => { if(view!=='software') { page=0; render(); } });
$('previous-page').addEventListener('click', () => { page--; render(); });
$('next-page').addEventListener('click', () => { page++; render(); });
$('clear-selection').addEventListener('click', () => { selected=null; render(); load(); });
$('close-detail').addEventListener('click', () => { detailGeneration++; $('detail').hidden=true; });
$('language').addEventListener('change', () => chooseLanguage($('language').value).catch(console.error));
$('badge').addEventListener('click', async () => {
  try { const pack=await get('/api/languages/tlh'); if (![...$('language').options].some(o=>o.value===pack.code)) $('language').add(new Option(pack.name,pack.code)); await chooseLanguage(pack.code); } catch(error) { console.error(error); }
});
(async () => {
  try {
    const languages = await get('/api/languages');
    for (const pack of languages) $('language').add(new Option(pack.name,pack.code));
    let preferred; try { preferred=localStorage.getItem('dmt.language'); } catch {}
    // Hidden packs are only selected after unlocking in this session.
    const requested = preferred || navigator.language;
    const code = languages.find(p=>p.code.toLowerCase()===requested.toLowerCase())?.code || languages.find(p=>p.code.split('-')[0]===requested.split('-')[0])?.code || 'en-US';
    await chooseLanguage(code); await load();
    const session=await fetch('/auth/session',{cache:'no-store'}); $('logout').hidden=!session.ok;
  } catch(error) { $('feedback').append(text('pre', String(error))); console.error(error); }
})();
