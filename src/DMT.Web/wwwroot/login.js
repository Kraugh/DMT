'use strict';
(async () => {
  const languages=await (await fetch('/api/languages')).json();
  const wanted=localStorage.getItem('dmt.language') || navigator.language;
  const code=languages.find(p=>p.code.toLowerCase()===wanted.toLowerCase())?.code || languages.find(p=>p.code.split('-')[0]===wanted.split('-')[0])?.code || 'en-US';
  const pack=await (await fetch('/api/languages/'+encodeURIComponent(code))).json();
  document.documentElement.lang=pack.code;
  for(const el of document.querySelectorAll('[data-key]'))el.textContent=pack.strings[el.dataset.key];
  document.getElementById('login').addEventListener('submit',async e=>{
    e.preventDefault(); const button=e.target.querySelector('button');button.disabled=true;
    try {
      const token=await (await fetch('/auth/token',{cache:'no-store'})).json();
      const response=await fetch('/auth/login',{method:'POST',headers:{'Content-Type':'application/json','X-DMT-CSRF':token.token},body:JSON.stringify({username:document.getElementById('username').value,password:document.getElementById('password').value})});
      if(response.ok)location.assign('/');
      else document.getElementById('error').textContent=pack.strings[response.status===503||response.status===429?'auth.rateLimit':'auth.failed'];
    } catch {document.getElementById('error').textContent=pack.strings['web.loadError'];}
    finally {document.getElementById('password').value='';button.disabled=false;}
  });
})();
