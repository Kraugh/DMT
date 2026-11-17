'use strict';
// Display only. Never change imported evidence or API values.
globalThis.DmtReportText = (() => {
  const legacy = new TextDecoder('windows-1252');
  const utf8 = new TextDecoder('utf-8', {fatal:true, ignoreBOM:true});
  const bytes = new Map();
  for (let i=0;i<256;i++) bytes.set(legacy.decode(new Uint8Array([i])),i);
  const score = value => (value.match(/[\u00c2\u00c3]|\u00e2(?=[\u0080-\u00bf\u20ac\u2018-\u2022])|\u00f0\u0178/gu) ?? []).length;
  function repairToken(token) {
    let current = token;
    for (let pass=0;pass<2 && score(current)>0;pass++) {
      const encoded=[];
      for (const character of current) {
        if (!bytes.has(character)) return current;
        encoded.push(bytes.get(character));
      }
      let decoded;
      try { decoded=utf8.decode(new Uint8Array(encoded)); } catch { return current; }
      if (score(decoded)>=score(current) || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(decoded)) return current;
      current=decoded;
    }
    return current;
  }
  function display(value) {
    const original=String(value ?? '');
    const corrected=original.replace(/\S+/gu,repairToken);
    return {original, text:corrected, corrected:corrected!==original};
  }
  return Object.freeze({display});
})();
