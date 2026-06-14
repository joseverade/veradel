namespace VeradeAddin.UI
{
    /// <summary>
    /// The HTML/CSS/JS page rendered inside <see cref="PartConfiguratorDialog"/>'s WebView2.
    /// Screen 1 is a catalog of generable parts. Picking "Bulón" opens a step-by-step wizard with a
    /// live dimensioned SVG (cotas) on the left that highlights the current step's dimensions, and
    /// manual inputs on the right: step 1 = head (Ø1, L1), step 2 = shank (Ø2, L2) → Crear.
    ///
    /// Styling is a clean, flat, professional light theme: 1px borders, subtle radius, a single
    /// accent colour, no gradients/animations; the preview is a black-on-white technical drawing.
    /// The SVG is rebuilt as a whole &lt;svg&gt; element inside a container div (reliable parsing),
    /// not via innerHTML on an existing svg node. Single quotes are used throughout so the markup
    /// lives in a C# verbatim string without escaping. On submit it posts
    /// <c>create|bolt|d1|l1|d2|l2</c> (or <c>cancel</c>) to the host.
    /// </summary>
    internal static class PartConfiguratorHtml
    {
        public const string Page = @"
<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<style>
  * { box-sizing: border-box; }
  html, body { margin: 0; height: 100%; }
  body {
    font-family: 'Segoe UI', system-ui, sans-serif; font-size: 13px;
    background: #EEF0F2; color: #1C1C1C; padding: 16px;
    -webkit-user-select: none; user-select: none;
  }
  h1 { font-size: 16px; font-weight: 600; margin: 0 0 2px; }
  .sub { font-size: 12px; color: #5A6068; margin-bottom: 16px; }
  .hidden { display: none !important; }

  /* ---- catalog ---- */
  .cards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
  .card {
    background: #FFFFFF; border: 1px solid #C9CDD2; border-radius: 4px; padding: 16px 12px;
    cursor: pointer; display: flex; flex-direction: column; align-items: center; gap: 8px; text-align: center;
  }
  .card:hover { border-color: #2F6FB3; }
  .card .ttl { font-size: 13px; font-weight: 600; }
  .card .desc { font-size: 11px; color: #5A6068; }
  .card.soon { cursor: default; color: #9AA0A6; }
  .card.soon:hover { border-color: #C9CDD2; }
  .badge { font-size: 10px; color: #5A6068; background: #EEF0F2; border: 1px solid #C9CDD2; border-radius: 3px; padding: 2px 8px; }

  /* ---- wizard ---- */
  .cfg { display: flex; gap: 16px; }
  .left {
    flex: 1; background: #FFFFFF; border: 1px solid #C9CDD2; border-radius: 4px;
    display: flex; align-items: center; justify-content: center; padding: 8px; min-height: 330px;
  }
  .left svg { width: 100%; height: 100%; display: block; }
  .right { width: 280px; display: flex; flex-direction: column; }

  .steps { display: flex; align-items: center; gap: 8px; margin-bottom: 16px; }
  .dot {
    width: 24px; height: 24px; border-radius: 50%; display: flex; align-items: center; justify-content: center;
    font-size: 12px; font-weight: 600; background: #FFFFFF; color: #5A6068; border: 1px solid #C9CDD2;
  }
  .dot.active { background: #2F6FB3; color: #FFFFFF; border-color: #2F6FB3; }
  .dot.done { background: #FFFFFF; color: #2F6FB3; border-color: #2F6FB3; }
  .bar { flex: 1; height: 1px; background: #C9CDD2; }

  .group { background: #FFFFFF; border: 1px solid #C9CDD2; border-radius: 4px; padding: 14px; margin-bottom: 12px; }
  .group h2 { font-size: 11px; text-transform: uppercase; letter-spacing: .4px; color: #5A6068; margin: 0 0 12px; font-weight: 600; }
  .field { margin-bottom: 12px; }
  .field:last-child { margin-bottom: 0; }
  label { display: block; font-size: 12px; color: #2A2E33; margin-bottom: 5px; }
  .inp { position: relative; }
  .inp input {
    width: 100%; background: #FFFFFF; color: #1C1C1C; font-family: inherit; font-size: 14px;
    padding: 8px 36px 8px 10px; border: 1px solid #C9CDD2; border-radius: 3px;
  }
  .inp input:focus { outline: none; border-color: #2F6FB3; }
  .inp input.bad { border-color: #C0392B; background: #FCEDEC; }
  .inp .u { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); font-size: 12px; color: #8A9099; pointer-events: none; }
  .err { min-height: 16px; font-size: 12px; color: #C0392B; margin: 2px 2px 12px; }
  .err.ok { color: #2F6FB3; }
  .actions { display: flex; gap: 8px; margin-top: auto; }
  button {
    font-family: inherit; font-size: 13px; border-radius: 4px; padding: 9px 14px; cursor: pointer;
    border: 1px solid #C9CDD2; background: #FFFFFF; color: #2A2E33;
  }
  button:hover { background: #F4F6F8; }
  button:disabled { color: #B0B5BB; cursor: default; background: #FFFFFF; }
  .ghost { flex: 0 0 auto; }
  .primary { flex: 1; background: #2F6FB3; border-color: #2F6FB3; color: #FFFFFF; font-weight: 600; }
  .primary:hover { background: #27598F; }
  .primary:disabled { background: #AFC4DE; border-color: #AFC4DE; color: #FFFFFF; }
  .dimtxt { font: 11px 'Segoe UI', sans-serif; }
</style>
</head>
<body>

  <!-- ===== screen 1: catalog ===== -->
  <section id='catalog'>
    <h1>Configurar pieza</h1>
    <div class='sub'>Elige la pieza que quieres generar</div>
    <div class='cards'>
      <div class='card' id='cardBolt'>
        <svg width='64' height='44' viewBox='0 0 64 44'>
          <rect x='8' y='7' width='9' height='30' fill='none' stroke='#1C1C1C' stroke-width='1.5'/>
          <rect x='17' y='13' width='39' height='18' fill='none' stroke='#1C1C1C' stroke-width='1.5'/>
        </svg>
        <div class='ttl'>BULON</div>
      </div>
      <div class='card soon'><span class='badge'>Próximamente</span><div class='desc'>Más piezas</div></div>
      <div class='card soon'><span class='badge'>Próximamente</span><div class='desc'>Más piezas</div></div>
    </div>
  </section>

  <!-- ===== screen 2: bolt wizard ===== -->
  <section id='bolt' class='hidden'>
    <h1>BULON</h1>
    <div class='sub' id='stepSub'></div>
    <div class='cfg'>
      <div class='left' id='canvas'></div>
      <div class='right'>
        <div class='steps'>
          <div class='dot' id='dot1'>1</div><div class='bar' id='bar'></div><div class='dot' id='dot2'>2</div>
        </div>

        <!-- step 1: head -->
        <div class='group' id='grp1'>
          <h2>Paso 1 &middot; Cabeza</h2>
          <div class='field'><label>Diámetro Ø1</label><div class='inp'><input id='d1' type='number' min='0' step='any' value='30'><span class='u'>mm</span></div></div>
          <div class='field'><label>Longitud L1</label><div class='inp'><input id='l1' type='number' min='0' step='any' value='5'><span class='u'>mm</span></div></div>
        </div>

        <!-- step 2: shank -->
        <div class='group hidden' id='grp2'>
          <h2>Paso 2 &middot; Vástago</h2>
          <div class='field'><label>Diámetro Ø2</label><div class='inp'><input id='d2' type='number' min='0' step='any' value='20'><span class='u'>mm</span></div></div>
          <div class='field'><label>Longitud L2</label><div class='inp'><input id='l2' type='number' min='0' step='any' value='25'><span class='u'>mm</span></div></div>
        </div>

        <div id='err' class='err'></div>
        <div class='actions'>
          <button class='ghost' id='btnBack'>Volver</button>
          <button class='primary' id='btnNext'>Siguiente</button>
        </div>
      </div>
    </div>
  </section>

<script>
  var catalog = document.getElementById('catalog');
  var bolt = document.getElementById('bolt');
  var canvas = document.getElementById('canvas');
  var step = 1;

  var ids = ['d1','l1','d2','l2'];
  var el = {};
  ids.forEach(function(k){ el[k] = document.getElementById(k); });
  var err = document.getElementById('err');
  var btnNext = document.getElementById('btnNext');
  var btnBack = document.getElementById('btnBack');
  var stepSub = document.getElementById('stepSub');

  function val(k){ var v = parseFloat(el[k].value); return isFinite(v) ? v : NaN; }
  function fmt(v){ return Math.round(v * 100) / 100; }

  document.getElementById('cardBolt').addEventListener('click', function(){
    catalog.classList.add('hidden'); bolt.classList.remove('hidden'); step = 1; render();
  });

  btnBack.addEventListener('click', function(){
    if (step === 2) { step = 1; render(); }
    else { bolt.classList.add('hidden'); catalog.classList.remove('hidden'); }
  });

  btnNext.addEventListener('click', function(){
    if (!stepValid()) return;
    if (step === 1) { step = 2; render(); }
    else { window.chrome.webview.postMessage(['create','bolt',val('d1'),val('l1'),val('d2'),val('l2')].join('|')); }
  });

  function stepValid(){
    var d1 = val('d1'), l1 = val('l1'), d2 = val('d2'), l2 = val('l2');
    if (step === 1) {
      var ok1 = d1 > 0 && l1 > 0;
      err.className = 'err'; err.textContent = ok1 ? '' : 'Introduce Ø1 y L1 mayores que 0.';
      if (ok1) { err.className = 'err ok'; err.textContent = 'Cabeza Ø' + fmt(d1) + ' × ' + fmt(l1) + ' mm'; }
      return ok1;
    }
    var posOk = d2 > 0 && l2 > 0;
    var bad = d1 > 0 && d2 > 0 && d2 >= d1;
    el.d2.classList.toggle('bad', bad);
    if (!posOk) { err.className = 'err'; err.textContent = 'Introduce Ø2 y L2 mayores que 0.'; return false; }
    if (bad) { err.className = 'err'; err.textContent = 'Ø2 debe ser menor que Ø1 (= ' + fmt(d1) + ').'; return false; }
    err.className = 'err ok'; err.textContent = 'Listo · longitud total ' + fmt(l1 + l2) + ' mm';
    return true;
  }

  function render(){
    document.getElementById('grp1').classList.toggle('hidden', step !== 1);
    document.getElementById('grp2').classList.toggle('hidden', step !== 2);
    document.getElementById('dot1').className = 'dot ' + (step > 1 ? 'done' : 'active');
    document.getElementById('dot2').className = 'dot ' + (step === 2 ? 'active' : '');
    btnBack.textContent = step === 1 ? 'Volver' : 'Atrás';
    btnNext.textContent = step === 1 ? 'Siguiente' : 'Crear';
    stepSub.textContent = step === 1
      ? 'Paso 1 de 2 · define la cabeza (revolución completa)'
      : 'Paso 2 de 2 · define el vástago · Ø2 debe ser menor que Ø1';
    update();
    (step === 1 ? el.d1 : el.d2).focus();
  }

  function update(){
    var ok = stepValid();
    btnNext.disabled = !ok;
    draw(val('d1'), val('l1'), val('d2'), val('l2'), step);
  }

  // --- dimensioned SVG (cotas): black-on-white technical drawing; active step's cotas in accent ---
  // Simple numeric attributes are left unquoted (valid HTML5); only values with spaces (points,
  // transform, viewBox) are single-quoted. The whole <svg> is rebuilt inside a div for reliable parsing.
  function ln(x1,y1,x2,y2,col,w){ return '<line x1='+x1.toFixed(1)+' y1='+y1.toFixed(1)+' x2='+x2.toFixed(1)+' y2='+y2.toFixed(1)+' stroke='+col+' stroke-width='+w+'/>'; }
  function tri(pts,col){ return '<polygon points=\'' + pts + '\' fill='+col+'/>'; }

  function hDim(x1,x2,y,text,col){
    var mx = ((x1+x2)/2).toFixed(1);
    var s = ln(x1,y,x2,y,col,1);
    s += tri(x1+','+y+' '+(x1+7)+','+(y-3)+' '+(x1+7)+','+(y+3), col);
    s += tri(x2+','+y+' '+(x2-7)+','+(y-3)+' '+(x2-7)+','+(y+3), col);
    s += '<text class=dimtxt fill='+col+' x='+mx+' y='+(y-5).toFixed(1)+' text-anchor=middle>'+text+'</text>';
    return s;
  }
  function vDim(y1,y2,x,text,col){
    var my = ((y1+y2)/2).toFixed(1), tx = (x-7).toFixed(1);
    var s = ln(x,y1,x,y2,col,1);
    s += tri(x+','+y1+' '+(x-3)+','+(y1+7)+' '+(x+3)+','+(y1+7), col);
    s += tri(x+','+y2+' '+(x-3)+','+(y2-7)+' '+(x+3)+','+(y2-7), col);
    s += '<text class=dimtxt fill='+col+' x='+tx+' y='+my+' text-anchor=middle transform=\'rotate(-90 '+tx+' '+my+')\'>'+text+'</text>';
    return s;
  }

  function draw(d1,l1,d2,l2,step){
    if (!(d1>0)) d1 = 30; if (!(l1>0)) l1 = 5; if (!(d2>0)) d2 = Math.min(20, d1*0.6); if (!(l2>0)) l2 = 25;
    var VBW=440, VBH=300, mL=82, mR=82, mT=80, mB=40;
    var rw=VBW-mL-mR, rh=VBH-mT-mB;
    var total=l1+l2, maxD=Math.max(d1,d2);
    var s=Math.min(rw/total, rh/maxD);
    var bw=total*s, ox=mL+(rw-bw)/2, cy=mT+rh/2;
    var w1=l1*s, w2=l2*s, h1=d1*s, h2=d2*s;
    var headL=ox, headR=ox+w1, shankR=ox+w1+w2;
    var headTop=cy-h1/2, headBot=cy+h1/2, shankTop=cy-h2/2, shankBot=cy+h2/2;

    var ink='#1C1C1C', active='#2F6FB3', muted='#7A8088', extc='#C2C6CC';
    var headCol = step === 1 ? active : muted;
    var shankCol = step === 2 ? active : muted;

    var out = '';
    out += '<line x1='+(headL-30).toFixed(1)+' y1='+cy.toFixed(1)+' x2='+(shankR+30).toFixed(1)+' y2='+cy.toFixed(1)+' stroke='+ink+' stroke-width=0.8 stroke-dasharray=\'7,3,2,3\'/>';
    out += '<rect x='+headL.toFixed(1)+' y='+headTop.toFixed(1)+' width='+w1.toFixed(1)+' height='+h1.toFixed(1)+' fill=none stroke='+ink+' stroke-width=1.5/>';
    out += '<rect x='+headR.toFixed(1)+' y='+shankTop.toFixed(1)+' width='+w2.toFixed(1)+' height='+h2.toFixed(1)+' fill=none stroke='+ink+' stroke-width=1.5/>';

    var yL = headTop - 32;
    out += ln(headL,headTop,headL,yL,extc,1) + ln(headR,headTop,headR,yL,extc,1) + ln(shankR,shankTop,shankR,yL,extc,1);
    var xQ1 = headL - 36;
    out += ln(headL,headTop,xQ1,headTop,extc,1) + ln(headL,headBot,xQ1,headBot,extc,1);
    var xQ2 = shankR + 36;
    out += ln(shankR,shankTop,xQ2,shankTop,extc,1) + ln(shankR,shankBot,xQ2,shankBot,extc,1);

    out += hDim(headL, headR, yL, 'L1 = '+fmt(l1), headCol);
    out += hDim(headR, shankR, yL, 'L2 = '+fmt(l2), shankCol);
    out += vDim(headTop, headBot, xQ1, 'Ø1 = '+fmt(d1), headCol);
    out += vDim(shankTop, shankBot, xQ2, 'Ø2 = '+fmt(d2), shankCol);

    canvas.innerHTML = '<svg viewBox=\'0 0 '+VBW+' '+VBH+'\' preserveAspectRatio=\'xMidYMid meet\'>' + out + '</svg>';
  }

  ids.forEach(function(k){ el[k].addEventListener('input', update); });
  document.addEventListener('keydown', function(e){
    if (e.key === 'Escape') { window.chrome.webview.postMessage('cancel'); }
    if (e.key === 'Enter' && !bolt.classList.contains('hidden') && !btnNext.disabled) { btnNext.click(); }
  });
</script>
</body>
</html>";
    }
}
