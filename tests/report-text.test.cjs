const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const context=vm.createContext({TextDecoder,Uint8Array});
vm.runInContext(fs.readFileSync('src/DMT.Web/wwwroot/report-text.js','utf8'),context);
const display=context.DmtReportText.display;
const original='servizio perch\u00c3\u00a9 \u00c3\u00a8 disabilitato';
assert.equal(display(original).text,'servizio perché è disabilitato');
assert.equal(display(original).original,original);
assert.equal(display(original).corrected,true);
for (const value of ['Già disponibile: è corretto','日本語 简体中文 Klingon','<img src=x onerror=alert(1)>','Ãngela','broken \ufffd','']) {
  assert.equal(display(value).text,value);
  assert.equal(display(value).corrected,false);
}
assert.equal(display('Già corretto, perch\u00c3\u00a9 corrotto').text,'Già corretto, perché corrotto');
assert.equal(display('l\u00e2\u20ac\u2122errore').text,'l’errore');
assert.equal(display('\u00c3\u0192\u00c2\u00a8').text,'è');
assert.equal(display(display(original).text).corrected,false);
console.log('PASS report text: 19 assertions');
