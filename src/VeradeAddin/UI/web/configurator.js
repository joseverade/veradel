// Lógica del configurador de pieza (Bulón + Eje). Cargado por configurator.html.
//
// La tabla DIN 471 la inyecta el host en window.DIN471 antes de este script
// (ver PartConfiguratorDialog.AddScriptToExecuteOnDocumentCreatedAsync). Si abres el .html
// suelto en el navegador, window.DIN471 no existe y se usa una tabla vacía.
//
// La vista previa de la izquierda es un <svg> que construye draw(): TODO atributo se entrecomilla
// vía mk(); un valor sin comillas antes de '/>' rompe el autocierre de la etiqueta y esta se traga
// a sus hermanos (ese era el bug del render). Nunca escribas etiquetas SVG a mano: usa mk()/LINE/RECT/PATH/POLY/TEXT.

var DIN471 = window.DIN471 || { rows: [] };

// Tabla DIN 509 (resources/data/din_509.txt): r, t1 y f valen para las formas E y F; t2 solo
// para la F (profundidad axial del rebaje en la cara del hombro). Rango de Ø del nivel MENOR:
// "más de min hasta max" (min < d ≤ max). ser: 'usual' = con esfuerzo usual, 'fatiga' = con
// resistencia a la fatiga aumentada (solo Ø > 18). Una fila por rango y serie.
var DIN509 = [
    { r: 0.1, t1: 0.1, f: 0.5, t2: 0.1, min: 0, max: 1.6, ser: 'usual' },
    { r: 0.2, t1: 0.1, f: 1.0, t2: 0.1, min: 1.6, max: 3, ser: 'usual' },
    { r: 0.4, t1: 0.2, f: 2.0, t2: 0.1, min: 3, max: 10, ser: 'usual' },
    { r: 0.6, t1: 0.2, f: 2.0, t2: 0.1, min: 10, max: 18, ser: 'usual' },
    { r: 0.6, t1: 0.3, f: 2.5, t2: 0.2, min: 18, max: 80, ser: 'usual' },
    { r: 1.0, t1: 0.4, f: 4.0, t2: 0.3, min: 80, max: Infinity, ser: 'usual' },
    { r: 1.0, t1: 0.2, f: 2.5, t2: 0.1, min: 18, max: 50, ser: 'fatiga' },
    { r: 1.6, t1: 0.3, f: 4.0, t2: 0.2, min: 50, max: 80, ser: 'fatiga' },
    { r: 2.5, t1: 0.4, f: 5.0, t2: 0.3, min: 80, max: 125, ser: 'fatiga' },
    { r: 4.0, t1: 0.5, f: 7.0, t2: 0.3, min: 125, max: Infinity, ser: 'fatiga' }
];
var UC_RAMP = 1 / Math.tan(15 * Math.PI / 180);   // avance axial por mm de t1 de la salida a 15°
var UC_RAMP8 = 1 / Math.tan(8 * Math.PI / 180);   // avance radial por mm de t2 de la salida a 8° (forma F)

// Tablas DIN 332 (puntos de centrado). VALORES ESTÁNDAR APROXIMADOS — REVISAR antes de producción.
// DIN 332-1 (formas A/B/R, sin rosca), por Ø de broca d1: d2 = Ø del avellanado 60°, d3 = Ø del
// avellanado de protección 120° (forma B), r = radio del flanco (forma R). min/max = rango de Ø del
// extremo del eje recomendado ("más de min, hasta max"); solo elige el tamaño por defecto.
var DIN332_1 = [
    { d1: 1.0, d2: 2.12, d3: 3.15, r: 0.3, min: 3, max: 6 },
    { d1: 1.6, d2: 3.35, d3: 5.0, r: 0.4, min: 6, max: 10 },
    { d1: 2.0, d2: 4.25, d3: 6.3, r: 0.5, min: 10, max: 18 },
    { d1: 2.5, d2: 5.3, d3: 8.0, r: 0.6, min: 18, max: 30 },
    { d1: 3.15, d2: 6.7, d3: 10.0, r: 0.8, min: 30, max: 50 },
    { d1: 4.0, d2: 8.5, d3: 12.5, r: 1.0, min: 50, max: 80 },
    { d1: 6.3, d2: 13.2, d3: 18.0, r: 1.6, min: 80, max: 120 },
    { d1: 10.0, d2: 21.2, d3: 28.0, r: 2.5, min: 120, max: Infinity }
];
// DIN 332-2 (forma D, roscado), por rosca métrica m: d1 = broca del núcleo, d2 = rebaje de alivio,
// d3 = avellanado de protección 120°. min/max = rango de Ø del extremo recomendado.
var DIN332_2 = [
    { m: 3, d1: 2.5, d2: 3.2, d3: 5.8, min: 6, max: 10 },
    { m: 4, d1: 3.3, d2: 4.3, d3: 7.4, min: 10, max: 18 },
    { m: 5, d1: 4.2, d2: 5.3, d3: 8.8, min: 18, max: 30 },
    { m: 6, d1: 5.0, d2: 6.4, d3: 10.5, min: 30, max: 50 },
    { m: 8, d1: 6.8, d2: 8.4, d3: 13.2, min: 50, max: 85 },
    { m: 10, d1: 8.5, d2: 10.5, d3: 16.3, min: 85, max: 130 },
    { m: 12, d1: 10.2, d2: 13.0, d3: 19.8, min: 130, max: 150 },
    { m: 16, d1: 14.0, d2: 17.0, d3: 25.3, min: 150, max: 220 },
    { m: 20, d1: 17.5, d2: 21.0, d3: 31.3, min: 220, max: 320 },
    { m: 24, d1: 21.0, d2: 25.0, d3: 38.0, min: 320, max: Infinity }
];
var CH_TAN30 = Math.tan(30 * Math.PI / 180);   // flanco del avellanado 60° (semiángulo 30°)
var CH_TAN60 = Math.tan(60 * Math.PI / 180);   // protección 120° y punta de broca 120° (semiángulo 60°)
// Longitudes rectas por defecto (mm); el host recibe el valor final ya calculado. Espejo de los
// defaults del builder: piloto/núcleo ≈ un diámetro, rosca útil ≈ 1.2·M, rebaje de alivio ≈ 0.4·d1.
function chPilotLen(d1) { return Math.max(0.8, d1); }
function chBoreLen(m) { return Math.max(1.2, 1.2 * m); }
function chCbLen(d1) { return Math.max(0.5, 0.4 * d1); }

var catalog = document.getElementById('catalog');
var bolt = document.getElementById('bolt');
var s = document.getElementById('canvas');
var step = 1;
var STEPS = 4;

var ids = ['d1', 'l1', 'd2', 'l2', 'p1', 'e1', 'd3', 'cang', 'csize'];
var el = {};
ids.forEach(function (k) { el[k] = document.getElementById(k); });
var gOn = document.getElementById('gOn'), dinOn = document.getElementById('dinOn');
var cOn = document.getElementById('cOn');
var dinNote = document.getElementById('dinNote');
var err = document.getElementById('err');
var btnNext = document.getElementById('btnNext');
var btnBack = document.getElementById('btnBack');
var stepSub = document.getElementById('stepSub');

function val(k) { var v = parseFloat(el[k].value); return isFinite(v) ? v : NaN; }
function fmt(v) { return Math.round(v * 100) / 100; }
function grooveOn() { return gOn.checked; }
function chamferOn() { return cOn.checked; }

document.getElementById('cardBolt').addEventListener('click', function () {
    catalog.classList.add('hidden'); bolt.classList.remove('hidden'); step = 1; render();
});

btnBack.addEventListener('click', function () {
    if (step > 1) {
        // retroceder desactiva la pieza opcional del paso que se abandona
        if (step === 3 && gOn.checked) { gOn.checked = false; document.getElementById('gFields').classList.add('off'); }
        if (step === 4 && cOn.checked) { cOn.checked = false; document.getElementById('cFields').classList.add('off'); }
        step--; render();
    }
    else { bolt.classList.add('hidden'); catalog.classList.remove('hidden'); }
});

btnNext.addEventListener('click', function () {
    if (!stepValid()) return;
    if (step < STEPS) { step++; render(); }
    else { submit(); }
});

function post(msg) {
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
}

function submit() {
    var g = grooveOn() ? 1 : 0, c = chamferOn() ? 1 : 0;
    var msg = ['create', 'bolt',
        val('d1'), val('l1'), val('d2'), val('l2'),
        g, val('p1') || 0, val('e1') || 0, val('d3') || 0,
        c, val('cang') || 0, val('csize') || 0].join('|');
    post(msg);
}

// ---- búsqueda DIN 471 (fila donde d == Ø2) ----
function dinLookup(d) {
    var rows = (DIN471 && DIN471.rows) ? DIN471.rows : [];
    for (var i = 0; i < rows.length; i++) {
        if (Math.abs(rows[i].d - d) < 1e-9) return rows[i];
    }
    return null;
}
function applyDin() {
    var on = dinOn.checked;
    el.d3.disabled = on; el.e1.disabled = on;
    if (!on) { dinNote.textContent = ''; dinNote.className = 'note'; return; }
    var d2 = val('d2'); var row = dinLookup(d2);
    if (row) {
        el.d3.value = row.d3; el.e1.value = row.m;
        dinNote.textContent = 'D3=' + fmt(row.d3) + ' · E1=' + fmt(row.m);
        dinNote.className = 'note';
    } else {
        el.d3.disabled = false; el.e1.disabled = false;
        dinNote.textContent = 'Sin dato DIN 471 para Ø' + (isFinite(d2) ? fmt(d2) : '?') + ' — introduce D3 manual.';
        dinNote.className = 'note warn';
    }
}
dinOn.addEventListener('change', function () { applyDin(); update(); });
gOn.addEventListener('change', function () {
    document.getElementById('gFields').classList.toggle('off', !gOn.checked);
    if (gOn.checked) applyDin();
    update();
});
cOn.addEventListener('change', function () {
    document.getElementById('cFields').classList.toggle('off', !cOn.checked);
    update();
});

function stepValid() {
    var d1 = val('d1'), l1 = val('l1'), d2 = val('d2'), l2 = val('l2');
    err.className = 'err'; err.textContent = '';

    if (step === 1) {
        var ok = d1 > 0 && l1 > 0;
        if (!ok) { err.textContent = 'Introduce Ø1 y L1 mayores que 0.'; return false; }
        err.className = 'err ok'; err.textContent = 'Cabeza Ø' + fmt(d1) + ' × ' + fmt(l1) + ' mm';
        return true;
    }
    if (step === 2) {
        var posOk = d2 > 0 && l2 > 0;
        var bad = d1 > 0 && d2 > 0 && d2 >= d1;
        el.d2.classList.toggle('bad', bad);
        if (!posOk) { err.textContent = 'Introduce Ø2 y L2 mayores que 0.'; return false; }
        if (bad) { err.textContent = 'Ø2 debe ser menor que Ø1 (= ' + fmt(d1) + ').'; return false; }
        err.className = 'err ok'; err.textContent = 'Vástago Ø' + fmt(d2) + ' × ' + fmt(l2) + ' mm';
        return true;
    }
    if (step === 3) {
        if (!grooveOn()) { err.className = 'err ok'; err.textContent = 'Sin ranura'; return true; }
        var p1 = val('p1'), e1 = val('e1'), d3 = val('d3');
        if (!(p1 > 0 && e1 > 0 && d3 > 0)) { err.textContent = 'P1, E1 y D3 deben ser mayores que 0.'; return false; }
        if (!(d3 < d2)) { el.d3.classList.add('bad'); err.textContent = 'D3 debe ser menor que Ø2 (= ' + fmt(d2) + ').'; return false; }
        el.d3.classList.remove('bad');
        if (!(p1 + e1 <= l2)) { err.textContent = 'La ranura se sale del vástago (P1 + E1 ≤ L2 = ' + fmt(l2) + ').'; return false; }
        err.className = 'err ok'; err.textContent = 'Ranura D3 ' + fmt(d3) + ' · ' + fmt(p1) + '…' + fmt(p1 + e1) + ' mm';
        return true;
    }
    // paso 4
    if (!chamferOn()) { err.className = 'err ok'; err.textContent = 'Sin chaflán · listo'; return true; }
    var ca = val('cang'), cs = val('csize');
    if (!(ca > 0 && ca < 90)) { err.textContent = 'El ángulo debe estar entre 0° y 90°.'; return false; }
    if (!(cs > 0)) { err.textContent = 'La medida del chaflán debe ser mayor que 0.'; return false; }
    var drop = cs * Math.tan(ca * Math.PI / 180);
    if (!(drop < d2 / 2)) { err.textContent = 'El chaflán es demasiado grande para el vástago.'; return false; }
    var limit = grooveOn() ? (val('p1') + val('e1')) : 0;
    if (!(l2 - cs >= limit)) {
        err.textContent = grooveOn() ? 'El chaflán se come la ranura (reduce la medida).' : 'El chaflán es más largo que el vástago.';
        return false;
    }
    err.className = 'err ok'; err.textContent = 'Chaflán ' + fmt(cs) + ' × ' + fmt(ca) + '° · listo';
    return true;
}

function render() {
    for (var i = 1; i <= STEPS; i++) {
        document.getElementById('grp' + i).classList.toggle('hidden', step !== i);
        var dot = document.getElementById('dot' + i);
        dot.className = 'dot ' + (step === i ? 'active' : (step > i ? 'done' : ''));
    }
    btnBack.textContent = step === 1 ? 'Volver' : 'Atrás';
    btnNext.textContent = step === STEPS ? 'Crear' : 'Siguiente';
    var subs = [
        '',
        'Paso 1 de 4 · define la cabeza (revolución completa)',
        'Paso 2 de 4 · define el vástago · Ø2 debe ser menor que Ø1',
        'Paso 3 de 4 · ranura opcional para anillo de retención',
        'Paso 4 de 4 · chaflán opcional en el extremo libre'];
    stepSub.textContent = subs[step];
    update();
    var focusEl = step === 1 ? el.d1 : step === 2 ? el.d2 : null;
    if (focusEl) focusEl.focus();
}

function update() {
    var ok = stepValid();
    btnNext.disabled = !ok;
    draw();
}

// --- SVG acotado (cotas): dibujo técnico negro-sobre-blanco; pieza activa en ROJO ---
// Cada elemento se crea con mk(): TODOS los atributos van entrecomillados, así cada etiqueta se
// autocierra. (Un valor sin comillas antes de '/>' mete el '/' en el valor, la etiqueta no cierra
// y se traga a sus hermanos — ese era el bug del render.)
var INK = '#1C1C1C', RED = '#E10000', BLU = '#2F6FB3';

function mk(tag, attrs, body) {
    var s = '<' + tag;
    for (var k in attrs) {
        var v = attrs[k];
        if (v === null || v === undefined) continue;
        s += ' ' + k + "='" + v + "'";
    }
    if (body === undefined) return s + ' />';
    return s + '>' + body + '</' + tag + '>';
}
function n1(v) { return Math.round(v * 10) / 10; }

function LINE(x1, y1, x2, y2, col, w, dash) {
    return mk('line', {
        x1: n1(x1), y1: n1(y1), x2: n1(x2), y2: n1(y2),
        stroke: col, 'stroke-width': w, 'stroke-dasharray': dash
    });
}
function RECT(x, y, w, h, col, sw) {
    return mk('rect', {
        x: n1(x), y: n1(y), width: n1(w), height: n1(h),
        fill: 'none', stroke: col, 'stroke-width': sw
    });
}
function PATH(pts, col, w, dash) {
    var d = 'M ' + n1(pts[0][0]) + ' ' + n1(pts[0][1]);
    for (var i = 1; i < pts.length; i++) d += ' L ' + n1(pts[i][0]) + ' ' + n1(pts[i][1]);
    return mk('path', { d: d, fill: 'none', stroke: col, 'stroke-width': w, 'stroke-linejoin': 'round', 'stroke-dasharray': dash });
}
// ranura tipo estadio (rectángulo + extremos semicirculares), eje horizontal en yc
function SLOT(xa, xb, yc, r, col, w, dash) {
    var d = 'M ' + n1(xa + r) + ' ' + n1(yc - r) +
        ' L ' + n1(xb - r) + ' ' + n1(yc - r) +
        ' A ' + n1(r) + ' ' + n1(r) + ' 0 0 1 ' + n1(xb - r) + ' ' + n1(yc + r) +
        ' L ' + n1(xa + r) + ' ' + n1(yc + r) +
        ' A ' + n1(r) + ' ' + n1(r) + ' 0 0 1 ' + n1(xa + r) + ' ' + n1(yc - r) + ' Z';
    return mk('path', { d: d, fill: 'none', stroke: col, 'stroke-width': w, 'stroke-linejoin': 'round', 'stroke-dasharray': dash });
}
function POLY(pts, col) {
    var p = '';
    for (var i = 0; i < pts.length; i++) p += (i ? ' ' : '') + n1(pts[i][0]) + ',' + n1(pts[i][1]);
    return mk('polygon', { points: p, fill: col });
}
function CIRC(cx, cy, r, col, w, dash) {
    return mk('circle', { cx: n1(cx), cy: n1(cy), r: n1(r), fill: 'none', stroke: col, 'stroke-width': w, 'stroke-dasharray': dash });
}
function TEXT(x, y, t, col, rot) {
    return mk('text', {
        x: n1(x), y: n1(y), fill: col, 'text-anchor': 'middle',
        'font-family': 'Segoe UI, sans-serif', 'font-size': '11',
        transform: rot ? ('rotate(-90 ' + n1(x) + ' ' + n1(y) + ')') : null
    }, t);
}
// punta de flecha rellena; dir = l|r|u|d (hacia dónde apunta)
function arrow(x, y, dir) {
    var a = dir === 'l' ? [[x, y], [x + 7, y - 3], [x + 7, y + 3]]
        : dir === 'r' ? [[x, y], [x - 7, y - 3], [x - 7, y + 3]]
            : dir === 'u' ? [[x, y], [x - 3, y + 7], [x + 3, y + 7]]
                : [[x, y], [x - 3, y - 7], [x + 3, y - 7]];
    return POLY(a, INK);
}
// texto anclado a la izquierda (para la etiqueta desplazada con directriz)
function TEXTL(x, y, t, col) {
    return mk('text', {
        x: n1(x), y: n1(y), fill: col, 'text-anchor': 'start',
        'font-family': 'Segoe UI, sans-serif', 'font-size': '11'
    }, t);
}
var ARROW_FIT = 18, DIM_EXT = 12;   // luz mínima para flechas dentro / prolongación cuando van fuera
// cota horizontal. Tres casos:
//  - hueco amplio: flechas dentro, texto centrado.
//  - hueco medio: flechas dentro, texto fuera a la derecha con directriz (raya = subrayado).
//  - hueco mínimo: flechas FUERA apuntando hacia dentro y línea de cota prolongada a ambos lados.
function hDim(x1, x2, y, text) {
    var xl = Math.min(x1, x2), xr = Math.max(x1, x2);
    var textW = text.length * 6;                 // ancho aprox. del texto (px)
    var out, dimRight;
    if (xr - xl >= ARROW_FIT) {
        out = LINE(xl, y, xr, y, INK, 1) + arrow(xl, y, 'l') + arrow(xr, y, 'r');
        if (xr - xl >= textW + 8) return out + TEXT((xl + xr) / 2, y - 5, text, INK, false);
        dimRight = xr;
    } else {
        out = LINE(xl - DIM_EXT, y, xr + DIM_EXT, y, INK, 1) + arrow(xl, y, 'r') + arrow(xr, y, 'l');
        dimRight = xr + DIM_EXT;
    }
    var lead = dimRight + 6;                      // arranque del texto desplazado
    return out + LINE(dimRight, y, lead + textW, y, INK, 1) + TEXTL(lead, y - 4, text, INK);
}
// cota vertical (Ø). Misma convención de flechas-fuera cuando el hueco es mínimo.
function vDim(y1, y2, x, text) {
    var yt = Math.min(y1, y2), yb = Math.max(y1, y2);
    var lbl = TEXT(x - 7, (yt + yb) / 2, text, INK, true);
    if (yb - yt >= ARROW_FIT)
        return LINE(x, yt, x, yb, INK, 1) + arrow(x, yt, 'u') + arrow(x, yb, 'd') + lbl;
    return LINE(x, yt - DIM_EXT, x, yb + DIM_EXT, INK, 1) + arrow(x, yt, 'd') + arrow(x, yb, 'u') + lbl;
}

function draw() {

    // Se obtienen los valores
    var d1 = val('d1'), lenght1 = val('l1'), d2 = val('d2'), length2 = val('l2');

    // Valores por defecto si d1, l1, d2 y l2 < 0
    if (!(d1 > 0)) d1 = 30; if (!(lenght1 > 0)) lenght1 = 5;
    if (!(d2 > 0)) d2 = Math.min(20, d1 * 0.6); if (!(length2 > 0)) length2 = 25;

    // grove: position 1, espesor 1, diameter
    var p1 = val('p1'), e1 = val('e1'), d3 = val('d3');

    var hasGrove = grooveOn() && p1 > 0 && e1 > 0 && d3 > 0 && d3 < d2 && (p1 + e1) <= length2;
    var chanflerAngle = val('cang'), chanflerHorizontalLength = val('csize');
    var cdrop = (chanflerHorizontalLength > 0 && chanflerAngle > 0 && chanflerAngle < 90) ? chanflerHorizontalLength * Math.tan(chanflerAngle * Math.PI / 180) : 0;
    var hasChamfer = chamferOn() && chanflerHorizontalLength > 0 && chanflerAngle > 0 && chanflerAngle < 90 && cdrop < d2 / 2 && (length2 - chanflerHorizontalLength) >= (hasGrove ? (p1 + e1) : 0);

    // viewbox witdth and height
    var VBW = 460, VBH = 320, marginLeft = 86, marginRight = 86, marginTop = 84, marginBottom = 46;
    var roomWidth = VBW - marginLeft - marginRight, roomHeight = VBH - marginTop - marginBottom;

    // Total lenght of the bolt and total height (max diameter)
    var totalBoltLength = lenght1 + length2, maxBoltDiameter = Math.max(d1, d2);

    // Scale min
    var scale = Math.min(roomWidth / totalBoltLength, roomHeight / maxBoltDiameter);

    // Midpoint of the top left line
    var boltWidth = totalBoltLength * scale
    var originX = marginLeft + (roomWidth - boltWidth) / 2
    var centerY = marginTop + roomHeight / 2;

    // Longitudes
    var widthHead1 = lenght1 * scale
    var widthBody2 = length2 * scale
    var h1 = d1 * scale
    var h2 = d2 * scale;

    // Points
    var headL = originX
    var headR = originX + widthHead1
    var tip = originX + widthHead1 + widthBody2;

    // 0,0 is the top left corner
    var headTop = centerY - h1 / 2
    var headBot = centerY + h1 / 2
    var shankTop = centerY - h2 / 2
    var shankBot = centerY + h2 / 2;

    // geometría de la ranura (pantalla)  (0,0 is the top left corner)
    var xgroove1 = headR + p1 * scale           // Posicion izquierda
    var xgroove2 = headR + (p1 + e1) * scale
    var h3 = d3 * scale
    var grooveTop = centerY - h3 / 2
    var grooveBot = centerY + h3 / 2;

    // geometría del chaflán (pantalla)
    var aS = chanflerHorizontalLength * scale
    var bS = cdrop * scale
    var xc = tip - aS
    var tipTop = shankTop + bS
    var tipBot = shankBot - bS;

    // bounding box del bulón completo: referencia ÚNICA para acotar (no se dibuja).
    // Así L1/L2/P1 comparten la misma Y de cota y no quedan a distinta altura.
    var boxTop = centerY - maxBoltDiameter * scale / 2;
    var boxBot = centerY + maxBoltDiameter * scale / 2;
    var boxLeft = headL, boxRight = tip;
    var dimY = boxTop - 34;          // línea de cota horizontal común
    var dimY2 = dimY - 18;           // segunda fila (cotas apiladas, p.ej. E1)

    // contorno superior del vástago (izq -> der), luego se espeja para el inferior
    var topPts = [[headR, shankTop]];
    if (hasGrove) { topPts.push([xgroove1, shankTop], [xgroove1, grooveTop], [xgroove2, grooveTop], [xgroove2, shankTop]); }
    if (hasChamfer) { topPts.push([xc, shankTop], [tip, tipTop]); }
    else { topPts.push([tip, shankTop]); }

    var out = '';
    out += LINE(headL - 30, centerY, tip + 30, centerY, INK, 0.8, '7,3,2,3');   // línea de eje
    out += RECT(headL, headTop, widthHead1, h1, INK, 1.5);               // cabeza

    // contorno del vástago: contorno superior, cara derecha, contorno inferior (espejo)
    var full = topPts.slice();
    full.push([tip, hasChamfer ? tipBot : shankBot]);
    for (var i = topPts.length - 1; i >= 0; i--) { full.push([topPts[i][0], 2 * centerY - topPts[i][1]]); }
    out += PATH(full, INK, 1.5);

    // aristas de revolución (caras vistas por el eje): SIEMPRE en negro para que la conexión
    // arriba-abajo se mantenga en todos los pasos; el paso activo las repinta en rojo encima.
    if (hasGrove) {
        out += LINE(xgroove1, shankTop, xgroove1, shankBot, INK, 1.5);
        out += LINE(xgroove2, shankTop, xgroove2, shankBot, INK, 1.5);
    }
    if (hasChamfer) {
        out += LINE(xc, shankTop, xc, shankBot, INK, 1.5);
    }

    // ----- pieza activa resaltada en ROJO -----
    if (step === 1) {
        out += RECT(headL, headTop, widthHead1, h1, RED, 1.8);
    } else if (step === 2) {
        out += RECT(headR, shankTop, widthBody2, h2, RED, 1.8);
    } else if (step === 3 && hasGrove) {
        out += PATH([[xgroove1, shankTop], [xgroove1, grooveTop], [xgroove2, grooveTop], [xgroove2, shankTop]], RED, 1.8);
        out += PATH([[xgroove1, shankBot], [xgroove1, grooveBot], [xgroove2, grooveBot], [xgroove2, shankBot]], RED, 1.8);
        // paredes de la ranura completas: cada punto superior se une con su simétrico inferior
        // de un solo trazo (shankTop→shankBot), cruzando fondo y eje (revolución 360°).
        out += LINE(xgroove1, shankTop, xgroove1, shankBot, RED, 1.8);
        out += LINE(xgroove2, shankTop, xgroove2, shankBot, RED, 1.8);
    } else if (step === 4 && hasChamfer) {
        out += LINE(xc, shankTop, tip, tipTop, RED, 1.8) + LINE(tip, tipTop, tip, tipBot, RED, 1.8) + LINE(xc, shankBot, tip, tipBot, RED, 1.8);
        // arista donde arranca el chaflán, completa por el eje (revolución 360°)
        out += LINE(xc, shankTop, xc, shankBot, RED, 1.8);
    }

    // ----- cotas del paso activo -----
    var xQL = boxLeft - 40, xQR = boxRight + 40;   // columnas de cotas Ø: izquierda / derecha
    if (step === 1) {
        // L1 sobre la línea de cota común; Ø1 a la izquierda del box
        out += LINE(headL, headTop, headL, dimY, INK, 1) + LINE(headR, headTop, headR, dimY, INK, 1);
        out += hDim(headL, headR, dimY, 'L1 = ' + fmt(lenght1));
        out += LINE(headL, headTop, xQL, headTop, INK, 1) + LINE(headL, headBot, xQL, headBot, INK, 1);
        out += vDim(headTop, headBot, xQL, 'Ø1 = ' + fmt(d1));
    } else if (step === 2) {
        // L2 sobre la misma línea de cota que L1; Ø2 a la derecha del box
        out += LINE(headR, shankTop, headR, dimY, INK, 1) + LINE(tip, shankTop, tip, dimY, INK, 1);
        out += hDim(headR, tip, dimY, 'L2 = ' + fmt(length2));
        out += LINE(tip, shankTop, xQR, shankTop, INK, 1) + LINE(tip, shankBot, xQR, shankBot, INK, 1);
        out += vDim(shankTop, shankBot, xQR, 'Ø2 = ' + fmt(d2));
    } else if (step === 3 && hasGrove) {
        // P1 en la línea común, E1 apilada encima; Ø3 a la derecha del box
        // xgroove1 sube hasta dimY2 para servir de apoyo a P1 (dimY) y a E1 (dimY2)
        out += LINE(headR, shankTop, headR, dimY, INK, 1) + LINE(xgroove1, shankTop, xgroove1, dimY2, INK, 1) + LINE(xgroove2, shankTop, xgroove2, dimY2, INK, 1);
        out += hDim(headR, xgroove1, dimY, 'P1 = ' + fmt(p1));
        out += hDim(xgroove1, xgroove2, dimY2, 'E1 = ' + fmt(e1));
        out += LINE(xgroove2, grooveTop, xQR, grooveTop, INK, 1) + LINE(xgroove2, grooveBot, xQR, grooveBot, INK, 1);
        out += vDim(grooveTop, grooveBot, xQR, 'Ø3 = ' + fmt(d3));
    } else if (step === 4 && hasChamfer) {
        // directriz DIAGONAL arriba-derecha desde la cara del chaflán; texto ENCIMA del tramo
        var mx = (xc + tip) / 2, myF = (shankTop + tipTop) / 2;
        var label = 'C' + fmt(chanflerHorizontalLength) + ' × ' + fmt(chanflerAngle) + '°';
        var dgn = 24, ex = mx + dgn, ey = myF - dgn;        // diagonal a 45°
        var tail = Math.max(40, label.length * 6);          // tramo horizontal bajo el texto
        out += LINE(mx, myF, ex, ey, INK, 1) + LINE(ex, ey, ex + tail, ey, INK, 1);
        out += POLY([[mx, myF], [mx + 7, myF - 3], [mx + 3, myF - 7]], INK);   // flecha tocando la cara
        out += TEXT(ex + tail / 2, ey - 5, label, INK, false);                 // texto encima del tramo
    }

    out = mk('svg', { viewBox: '0 0 ' + VBW + ' ' + VBH, preserveAspectRatio: 'xMidYMid meet' }, out);
    canvas.innerHTML = out;
}

ids.forEach(function (k) {
    el[k].addEventListener('input', function () {
        if (k === 'd2' && dinOn.checked) applyDin();
        update();
    });
});
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { post('cancel'); }
    if (e.key === 'Enter' && !bolt.classList.contains('hidden') && !btnNext.disabled) { btnNext.click(); }
    if (e.key === 'Enter' && !shaftSec.classList.contains('hidden')) {
        if (editKey && !btnKeyOk.disabled) { btnKeyOk.click(); }
        else if (editGrv && !btnGrvOk.disabled) { btnGrvOk.click(); }
        else if (editUc && !btnUcOk.disabled) { btnUcOk.click(); }
        else if (editCh && !btnChOk.disabled) { btnChOk.click(); }
        else if (!editKey && !editGrv && !editUc && !editCh && !shaftNextBtn.disabled) { shaftNextBtn.click(); }
    }
});

// ================================================================
// EJE — asistente de 3 pasos.
//  Paso 1 · Cuerpo: n niveles Ø × L de izquierda a derecha. Niveles
//  consecutivos con el mismo Ø se funden en un tramo (la revolución no
//  admite un escalón de altura cero) y la frontera se materializa en
//  SolidWorks como LÍNEA DE DIVISIÓN (aquí discontinua roja). Espejo de
//  ShaftSpec.GetMergedSegments() del host.
//  Paso 2 · Chavetas (DIN 6885 forma A): lista editable. Posición =
//  arista de referencia (clic en el dibujo o combo) + cota con signo
//  (nunca 0) que SIEMPRE apunta al extremo izquierdo de la chaveta:
//  x1 = arista + cota (negativa = el extremo izq queda a la izquierda
//  de la arista y la chaveta puede cruzarla). Alternativas (ctr): el
//  CENTRO del arco izq (ctr=1) o der (ctr=2) cae en la arista (ubicación,
//  sin cota); entonces L = cota centro anclado→extremo opuesto y el largo
//  total es L + b/2. Puede sobresalir PARCIALMENTE por un extremo del eje
//  (chavetero abierto): basta con que algún tramo quede sobre el eje.
//  Profundidad t radial desde la superficie del Ø de referencia (combo
//  cuando la chaveta atraviesa 2+ niveles). Vista de sección con ángulo
//  inicial y nº de copias polares. Espejo de ShaftSpec.ValidateKeyway.
//  Paso 3 · Ranuras de anillo (DIN 471): lista editable, misma mecánica
//  de posición que las chavetas (x1 = arista + cota, a la pared izq).
//  E1 × D3 a mano o autorrellenados desde window.DIN471 con el Ø del
//  nivel donde cae la ranura. Debe caber entera en un nivel (las líneas
//  de división no son paredes). Espejo de ShaftSpec.ValidateGroove.
//  Paso 4 · Entalladuras (DIN 509 E/F): una por hombro (cambio de Ø)
//  como máximo, talladas en el nivel MENOR contra la cara del hombro
//  (la F rebaja además la cara t2 con salida a 8°). Sin cota: la
//  posición es el hombro mismo. Se pregunta tipo (E/F) y solicitación
//  (usual/fatiga); la medida sale de DIN509 con el Ø menor. Espejo de
//  ShaftSpec.ValidateUndercut.
//  Paso 5 · Puntos de centrado (DIN 332): uno por extremo como máximo,
//  corte de revolución coaxial en la cara. Formas A (avellanado 60°),
//  B (60° + protección 120°), R (flanco de radio) — DIN 332-1 — y D
//  (roscado, DIN 332-2). Se elige extremo (izq/der), tipo y tamaño; la
//  medida sale de DIN332_1/DIN332_2 según el Ø del extremo. Espejo de
//  ShaftSpec.ValidateCenterHole.
//  Protocolo: create|shaft|n|d,l...|K|<chavetas×9>|G|<ranuras×4>|
//             U|<entalladuras×6>|C|<puntos×9: extremo,tipo,d1,d2,d3,lp,rarc,lc,rosca>
// ================================================================

var shaftSec = document.getElementById('shaft');
var shaftCanvasEl = document.getElementById('shaftCanvas');
var shaftNEl = document.getElementById('shaftN');
var levelListEl = document.getElementById('levelList');
var shaftErrEl = document.getElementById('shaftErr');
var shaftSplitNoteEl = document.getElementById('shaftSplitNote');
var shaftNextBtn = document.getElementById('shaftNext');
var shaftBackBtn = document.getElementById('shaftBack');
var shaftSubEl = document.getElementById('shaftSub');
var keyListEl = document.getElementById('keyList');
var keyFormEl = document.getElementById('keyForm');
var keySectionEl = document.getElementById('keySection');
var krefWrapEl = document.getElementById('krefWrap');
var btnAddKey = document.getElementById('btnAddKey');
var btnKeyOk = document.getElementById('btnKeyOk');
var btnKeyDel = document.getElementById('btnKeyDel');
var kEdgeSel = document.getElementById('kedge');
var kRefSel = document.getElementById('krefd');
var kIds = ['kb', 'kl', 'koff', 'kdepth', 'kang', 'kcnt'];
var kEl = {};
kIds.forEach(function (k) { kEl[k] = document.getElementById(k); });
var kAnchorSel = document.getElementById('kanchor');
var kOffWrap = document.getElementById('koffWrap');
var klLabelEl = document.getElementById('klLabel');
var grvListEl = document.getElementById('grvList');
var grvFormEl = document.getElementById('grvForm');
var btnAddGrv = document.getElementById('btnAddGrv');
var btnGrvOk = document.getElementById('btnGrvOk');
var btnGrvDel = document.getElementById('btnGrvDel');
var gEdgeSel = document.getElementById('gedge');
var gDinChk = document.getElementById('gdin');
var gDinNote = document.getElementById('gdinNote');
var gIds = ['goff', 'ge1', 'gd3'];
var gEl = {};
gIds.forEach(function (g) { gEl[g] = document.getElementById(g); });
var ucListEl = document.getElementById('ucList');
var ucFormEl = document.getElementById('ucForm');
var btnAddUc = document.getElementById('btnAddUc');
var btnUcOk = document.getElementById('btnUcOk');
var btnUcDel = document.getElementById('btnUcDel');
var uEdgeSel = document.getElementById('uedge');
var ucTypeSel = document.getElementById('utype');
var ucSerieSel = document.getElementById('userie');
var ucNoteEl = document.getElementById('ucNote');
var chListEl = document.getElementById('chList');
var chFormEl = document.getElementById('chForm');
var btnAddCh = document.getElementById('btnAddCh');
var btnChOk = document.getElementById('btnChOk');
var btnChDel = document.getElementById('btnChDel');
var cEndSel = document.getElementById('cend');
var cTypeSel = document.getElementById('ctype');
var csizeSel = document.getElementById('csizeSel');
var chNoteEl = document.getElementById('chNote');

var shaftLevels = [{ d: 20, l: 30 }, { d: 30, l: 40 }, { d: 20, l: 30 }];
var shaftFocus = 0;      // nivel con foco en el paso 1 (rojo + cotas)
var shaftStep = 1;       // 1 = cuerpo, 2 = chavetas, 3 = ranuras de anillo
var shaftKeys = [];      // chavetas aceptadas
var editKey = null;      // chaveta en edición (copia de trabajo) o null
var editIdx = -1;        // índice en shaftKeys si se edita una existente; -1 = nueva
var shaftGrvs = [];      // ranuras de anillo aceptadas
var editGrv = null;      // ranura en edición (copia de trabajo) o null
var editGrvIdx = -1;     // índice en shaftGrvs si se edita una existente; -1 = nueva
var shaftUcs = [];       // entalladuras DIN 509 aceptadas
var editUc = null;       // entalladura en edición (copia de trabajo) o null
var editUcIdx = -1;      // índice en shaftUcs si se edita una existente; -1 = nueva
var shaftChs = [];       // puntos de centrado DIN 332 aceptados
var editCh = null;       // punto en edición (copia de trabajo) o null
var editChIdx = -1;      // índice en shaftChs si se edita uno existente; -1 = nuevo

var KTOL = 1e-6;

document.getElementById('cardShaft').addEventListener('click', function () {
    catalog.classList.add('hidden'); shaftSec.classList.remove('hidden');
    shaftStep = 1; shaftFocus = 0; closeKeyForm(); closeGrvForm(); closeUcForm(); closeChForm(); buildLevelRows(); shaftRender();
});
shaftBackBtn.addEventListener('click', function () {
    if (editKey) { closeKeyForm(); shaftUpdate(); return; }
    if (editGrv) { closeGrvForm(); shaftUpdate(); return; }
    if (editUc) { closeUcForm(); shaftUpdate(); return; }
    if (editCh) { closeChForm(); shaftUpdate(); return; }
    if (shaftStep === 5) { shaftStep = 4; shaftRender(); return; }
    if (shaftStep === 4) { shaftStep = 3; shaftRender(); return; }
    if (shaftStep === 3) { shaftStep = 2; shaftRender(); return; }
    if (shaftStep === 2) { shaftStep = 1; shaftRender(); return; }
    shaftSec.classList.add('hidden'); catalog.classList.remove('hidden');
});
shaftNextBtn.addEventListener('click', function () {
    if (shaftStep === 1) {
        if (shaftValidate() !== null) return;
        shaftStep = 2; shaftRender(); return;
    }
    if (shaftStep === 2) {
        if (editKey || shaftFirstBadKey() !== null) return;
        shaftStep = 3; shaftRender(); return;
    }
    if (shaftStep === 3) {
        if (editGrv || shaftFirstBadGrv() !== null) return;
        shaftStep = 4; shaftRender(); return;
    }
    if (shaftStep === 4) {
        if (editUc || shaftFirstBadUc() !== null) return;
        shaftStep = 5; shaftRender(); return;
    }
    if (editCh || shaftFirstBadCh() !== null) return;
    shaftSubmit();
});
shaftNEl.addEventListener('input', function () {
    var n = parseInt(shaftNEl.value, 10);
    if (!isFinite(n)) return;
    n = Math.max(1, Math.min(20, n));
    if (String(n) !== shaftNEl.value) shaftNEl.value = n;   // re-clampa lo tecleado (p.ej. 50 → 20)
    while (shaftLevels.length < n) {
        var prev = shaftLevels[shaftLevels.length - 1];
        shaftLevels.push({ d: prev ? prev.d : 20, l: prev ? prev.l : 30 });
    }
    shaftLevels.length = n;
    if (shaftFocus >= n) shaftFocus = n - 1;
    buildLevelRows(); shaftUpdate();
});

function shaftSubmit() {
    var msg = ['create', 'shaft', shaftLevels.length];
    for (var i = 0; i < shaftLevels.length; i++) { msg.push(shaftLevels[i].d, shaftLevels[i].l); }
    msg.push(shaftKeys.length);
    for (var k = 0; k < shaftKeys.length; k++) {
        var ky = shaftKeys[k];
        msg.push(ky.b, ky.l, ky.edge, ky.off, ky.depth, ky.refd, ky.ang, ky.cnt, ky.ctr || 0);
    }
    msg.push(shaftGrvs.length);
    for (var g = 0; g < shaftGrvs.length; g++) {
        var gv = shaftGrvs[g];
        msg.push(gv.e1, gv.d3, gv.edge, gv.off);
    }
    msg.push(shaftUcs.length);
    for (var u = 0; u < shaftUcs.length; u++) {
        var uv = shaftUcs[u];
        msg.push(uv.bnd, uv.form, uv.r, uv.t1, uv.f, uv.t2);
    }
    msg.push(shaftChs.length);
    for (var c = 0; c < shaftChs.length; c++) {
        var cv = shaftChs[c];
        msg.push(cv.end, cv.form, cv.d1, cv.d2, cv.d3, cv.lp, cv.rarc, cv.lc, cv.thread);
    }
    post(msg.join('|'));
}

// ---- paso 1: niveles ----------------------------------------------------

// (re)construye las filas de niveles conservando los valores actuales
function buildLevelRows() {
    var html = '';
    for (var i = 0; i < shaftLevels.length; i++) {
        html += '<div class="lvlrow" id="lvlrow' + i + '">' +
            '<span class="idx">' + (i + 1) + '</span>' +
            '<div class="inp"><input id="lvld' + i + '" type="number" min="0" step="any" value="' + shaftLevels[i].d + '"><span class="u">Ø</span></div>' +
            '<div class="inp"><input id="lvll' + i + '" type="number" min="0" step="any" value="' + shaftLevels[i].l + '"><span class="u">L</span></div>' +
            '</div>';
    }
    levelListEl.innerHTML = html;
    for (var j = 0; j < shaftLevels.length; j++) {
        (function (k) {
            var dEl = document.getElementById('lvld' + k), lEl = document.getElementById('lvll' + k);
            function grab() {
                var d = parseFloat(dEl.value), l = parseFloat(lEl.value);
                shaftLevels[k].d = isFinite(d) ? d : NaN;
                shaftLevels[k].l = isFinite(l) ? l : NaN;
                shaftUpdate();
            }
            function focus() { shaftFocus = k; markFocusRow(); drawShaft(); }
            dEl.addEventListener('input', grab); lEl.addEventListener('input', grab);
            dEl.addEventListener('focus', focus); lEl.addEventListener('focus', focus);
        })(j);
    }
    markFocusRow();
}
function markFocusRow() {
    for (var i = 0; i < shaftLevels.length; i++) {
        var row = document.getElementById('lvlrow' + i);
        if (row) row.classList.toggle('focus', i === shaftFocus);
    }
}

// null = válido; si no, el motivo (mismas reglas que ShaftSpec.Validate del host)
function shaftValidate() {
    if (shaftLevels.length < 1) return 'El eje necesita al menos un nivel.';
    for (var i = 0; i < shaftLevels.length; i++) {
        if (!(shaftLevels[i].d > 0) || !(shaftLevels[i].l > 0)) {
            return 'Nivel ' + (i + 1) + ': Ø y L deben ser mayores que 0.';
        }
    }
    return null;
}
// fronteras x (mm desde la izquierda): 0, tras nivel 1, ..., total
function shaftBounds() {
    var xs = [0], x = 0;
    for (var i = 0; i < shaftLevels.length; i++) { x += shaftLevels[i].l; xs.push(x); }
    return xs;
}
function shaftTotal() { var xs = shaftBounds(); return xs[xs.length - 1]; }
// posiciones x donde dos niveles consecutivos comparten Ø (línea de división)
function shaftSplits() {
    var splits = [], x = 0;
    for (var i = 0; i < shaftLevels.length - 1; i++) {
        x += shaftLevels[i].l;
        if (Math.abs(shaftLevels[i].d - shaftLevels[i + 1].d) < 1e-9) splits.push(x);
    }
    return splits;
}

// ---- paso 2: chavetas ---------------------------------------------------

function keyX1(k) {
    var xs = shaftBounds();
    var xe = xs[Math.max(0, Math.min(k.edge, xs.length - 1))];
    // ctr=1: centro del arco IZQ en la arista → extremo izq = arista − b/2
    if (k.ctr === 1) return xe - (k.b > 0 ? k.b / 2 : 0);
    // ctr=2: centro del arco DER en la arista; L = centro→extremo IZQ → x1 = arista − L
    if (k.ctr === 2) return xe - k.l;
    // la cota SIEMPRE apunta al extremo izquierdo: x1 = arista + cota (negativa = a la izq)
    return xe + k.off;
}
// longitud TOTAL extremo a extremo: L, o L + b/2 en los modos de centro anclado
function keySpan(k) {
    return k.ctr ? k.l + (k.b > 0 ? k.b / 2 : 0) : k.l;
}
// diámetros (únicos, en orden) de los niveles que la chaveta atraviesa
function keySpannedDiams(k) {
    var x1 = keyX1(k), x2 = x1 + keySpan(k), xs = shaftBounds(), out = [];
    if (!isFinite(x1) || !isFinite(x2)) return out;
    for (var i = 0; i < shaftLevels.length; i++) {
        if (xs[i + 1] <= x1 + KTOL || xs[i] >= x2 - KTOL) continue;
        var d = shaftLevels[i].d;
        var seen = false;
        for (var j = 0; j < out.length; j++) if (Math.abs(out[j] - d) < 1e-9) seen = true;
        if (!seen) out.push(d);
    }
    return out;
}
// null = válida; espejo de ShaftSpec.ValidateKeyway del host
function validateKey(k) {
    if (!(k.b > 0) || !(k.l > 0) || !(k.depth > 0)) return 'Ancho, largo y profundidad deben ser > 0.';
    if (!k.ctr && !(k.l > k.b)) return 'El largo debe ser mayor que el ancho (forma A).';
    if (k.ctr && !(k.l > k.b / 2)) return 'La cota centro→extremo debe ser mayor que b/2.';
    if (!(k.edge >= 0) || !(k.edge <= shaftLevels.length)) return 'Arista de referencia no válida (¿cambiaste los niveles?).';
    if (!k.ctr && !(Math.abs(k.off) > KTOL)) return 'La cota de posición no puede ser 0.';
    if (!(k.cnt >= 1)) return 'El número de chavetas debe ser al menos 1.';
    var xs = shaftBounds(), total = shaftTotal();
    var x1 = keyX1(k), x2 = x1 + keySpan(k);
    // sobresalir PARCIALMENTE por un extremo es válido (chavetero abierto): basta con que
    // algún tramo quede sobre el eje…
    if (!(x2 > KTOL) || !(x1 < total - KTOL)) return 'La chaveta queda fuera del eje.';
    // …pero un extremo del arco justo EN la cara del extremo deja pared de espesor cero
    if (Math.abs(x1) <= KTOL || Math.abs(x2) <= KTOL ||
        Math.abs(x1 - total) <= KTOL || Math.abs(x2 - total) <= KTOL) {
        return 'Un extremo del arco cae justo en la cara del extremo del eje.';
    }
    for (var i = 1; i < shaftLevels.length; i++) {
        if (Math.abs(shaftLevels[i - 1].d - shaftLevels[i].d) < 1e-9) continue; // split line, no es pared
        var xc = xs[i];
        if (Math.abs(x1 - xc) <= KTOL || Math.abs(x2 - xc) <= KTOL) {
            return 'Un extremo del arco cae justo en el cambio de nivel (x = ' + fmt(xc) + ').';
        }
    }
    var diams = keySpannedDiams(k), refOk = false;
    for (var j = 0; j < diams.length; j++) if (Math.abs(diams[j] - k.refd) < 1e-9) refOk = true;
    if (!refOk) return 'El Ø de referencia debe ser el de un nivel que la chaveta atraviesa.';
    if (!(k.depth < k.refd / 2)) return 'La profundidad debe ser menor que el radio de referencia (' + fmt(k.refd / 2) + ').';
    if (!(k.b < k.refd)) return 'El ancho debe ser menor que el Ø de referencia.';
    return null;
}
// primera chaveta aceptada que dejó de ser válida (p.ej. tras cambiar niveles); null = todas bien
function shaftFirstBadKey() {
    for (var i = 0; i < shaftKeys.length; i++) {
        var msg = validateKey(shaftKeys[i]);
        if (msg !== null) return 'Chaveta ' + (i + 1) + ': ' + msg;
    }
    return null;
}

btnAddKey.addEventListener('click', function () {
    var total = shaftTotal();
    var l = Math.min(20, total * 0.4), b = Math.min(6, l);
    var off = total / 2 - l / 2;              // arranca centrada en el eje visible
    if (!(Math.abs(off) > KTOL)) off = 1;
    var k = { b: b, l: l, edge: 0, off: off, depth: 3.5, refd: 0, ang: 0, cnt: 1, ctr: 0 };
    var diams = keySpannedDiams(k);
    k.refd = diams.length ? diams[0] : (shaftLevels[0].d || 0);
    if (!(k.depth < k.refd / 2)) k.depth = Math.max(0.5, fmt(k.refd / 4));
    editKey = k; editIdx = -1;
    openKeyForm(); shaftUpdate();
});
btnKeyOk.addEventListener('click', function () {
    if (!editKey || validateKey(editKey) !== null) return;
    if (editIdx >= 0) shaftKeys[editIdx] = editKey; else shaftKeys.push(editKey);
    closeKeyForm(); shaftUpdate();
});
btnKeyDel.addEventListener('click', function () {
    if (editIdx >= 0) shaftKeys.splice(editIdx, 1);
    closeKeyForm(); shaftUpdate();
});
kIds.forEach(function (id) {
    kEl[id].addEventListener('input', function () {
        if (!editKey) return;
        var v = parseFloat(kEl[id].value);
        if (id === 'kb') editKey.b = v;
        else if (id === 'kl') editKey.l = v;
        else if (id === 'koff') editKey.off = v;
        else if (id === 'kdepth') editKey.depth = v;
        else if (id === 'kang') editKey.ang = isFinite(v) ? v : 0;
        else if (id === 'kcnt') editKey.cnt = Math.max(1, Math.round(v) || 1);
        syncRefOptions();
        shaftUpdate();
    });
});
kEdgeSel.addEventListener('change', function () {
    if (!editKey) return;
    editKey.edge = parseInt(kEdgeSel.value, 10) || 0;
    editKey.off = 3; kEl.koff.value = 3;        // default al elegir arista
    syncRefOptions(); shaftUpdate();
});
kAnchorSel.addEventListener('change', function () {
    if (!editKey) return;
    editKey.ctr = parseInt(kAnchorSel.value, 10) || 0;
    syncAnchorUi();
    syncRefOptions(); shaftUpdate();
});
// campo de cota solo en modo cota; la etiqueta de L cambia de significado en modos de centro
function syncAnchorUi() {
    var ctr = editKey ? (editKey.ctr || 0) : 0;
    kOffWrap.style.display = ctr ? 'none' : '';
    klLabelEl.textContent = ctr ? 'Largo L (centro→extremo)' : 'Largo L (total)';
}
kRefSel.addEventListener('change', function () {
    if (!editKey) return;
    editKey.refd = parseFloat(kRefSel.value) || 0;
    shaftUpdate();
});
// clic en una arista del dibujo = elegirla como referencia (chaveta, ranura o entalladura);
// clic en la botonera de zoom = zoom/pan del alzado (activo en todos los pasos)
shaftCanvasEl.addEventListener('click', function (e) {
    var t = e.target;
    if (!t || !t.getAttribute) return;
    var zAct = t.getAttribute('data-z');
    if (zAct) { shaftZoomAction(zAct); return; }
    if (!editKey && !editGrv && !editUc) return;
    var idx = t.getAttribute('data-edge');
    if (idx === null) return;
    if (editKey) {
        editKey.edge = parseInt(idx, 10);
        kEdgeSel.value = String(editKey.edge);
        editKey.off = 3; kEl.koff.value = 3;    // default al elegir arista
        syncRefOptions();
    } else if (editGrv) {
        editGrv.edge = parseInt(idx, 10);
        gEdgeSel.value = String(editGrv.edge);
        editGrv.off = 3; gEl.goff.value = 3;    // default al elegir arista
        applyGrvDin();
    } else {
        var bnd = parseInt(idx, 10);
        if (!shoulderInfo(bnd)) return;         // solo hombros (cambio de Ø)
        editUc.bnd = bnd;
        uEdgeSel.value = String(bnd);
        syncUcSize();
    }
    shaftUpdate();
});

function openKeyForm() {
    kEl.kb.value = editKey.b; kEl.kl.value = editKey.l; kEl.koff.value = editKey.off;
    kEl.kdepth.value = editKey.depth; kEl.kang.value = editKey.ang; kEl.kcnt.value = editKey.cnt;
    kAnchorSel.value = String(editKey.ctr || 0);
    syncAnchorUi();
    var xs = shaftBounds(), opts = '';
    for (var i = 0; i < xs.length; i++) {
        var label = i === 0 ? 'Extremo izquierdo (x = 0)'
            : i === xs.length - 1 ? 'Extremo derecho (x = ' + fmt(xs[i]) + ')'
                : 'Cambio nivel ' + i + '→' + (i + 1) + ' (x = ' + fmt(xs[i]) + ')';
        opts += '<option value="' + i + '">' + label + '</option>';
    }
    kEdgeSel.innerHTML = opts;
    kEdgeSel.value = String(editKey.edge);
    syncRefOptions();
    keyFormEl.classList.remove('hidden');
    btnAddKey.classList.add('hidden');
    btnKeyDel.style.display = editIdx >= 0 ? '' : 'none';
    renderKeyList();
}
function closeKeyForm() {
    editKey = null; editIdx = -1;
    keyFormEl.classList.add('hidden');
    btnAddKey.classList.remove('hidden');
    renderKeyList();
}
// combo del Ø de referencia = diámetros que la chaveta atraviesa ahora mismo
function syncRefOptions() {
    if (!editKey) return;
    var diams = keySpannedDiams(editKey);
    if (!diams.length) { kRefSel.innerHTML = '<option value="0">—</option>'; kRefSel.disabled = true; return; }
    var keep = false, opts = '';
    for (var i = 0; i < diams.length; i++) {
        opts += '<option value="' + diams[i] + '">Ø ' + fmt(diams[i]) + ' mm</option>';
        if (Math.abs(diams[i] - editKey.refd) < 1e-9) keep = true;
    }
    kRefSel.innerHTML = opts;
    if (!keep) editKey.refd = diams[0];
    kRefSel.value = String(editKey.refd);
    kRefSel.disabled = diams.length === 1;
    krefWrapEl.style.display = '';
}
function renderKeyList() {
    var html = '';
    for (var i = 0; i < shaftKeys.length; i++) {
        var k = shaftKeys[i], x1 = keyX1(k);
        var pos = isFinite(x1) ? fmt(x1) + '…' + fmt(x1 + keySpan(k)) : '—';
        html += '<div class="keyitem' + (i === editIdx ? ' editing' : '') + '" data-key="' + i + '">' +
            '<span>' + (i + 1) + ' · b' + fmt(k.b) + '×L' + fmt(k.l) + ' · x ' + pos +
            ' · t' + fmt(k.depth) + (k.ctr === 1 ? ' · centro izq=arista' : k.ctr === 2 ? ' · centro der=arista' : '') +
            (k.cnt > 1 ? ' · ' + k.cnt + ' uds' : '') + (k.ang ? ' · ' + fmt(k.ang) + '°' : '') + '</span>' +
            '<span class="kx" data-del="' + i + '">✕</span></div>';
    }
    keyListEl.innerHTML = html;
    var items = keyListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-del');
            if (del !== null && del !== undefined) {
                shaftKeys.splice(parseInt(del, 10), 1);
                closeKeyForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editIdx = parseInt(this.getAttribute('data-key'), 10);
            editKey = JSON.parse(JSON.stringify(shaftKeys[editIdx]));
            openKeyForm(); shaftUpdate();
        });
    }
}

// ---- paso 3: ranuras de anillo (DIN 471) --------------------------------

// pared izquierda de la ranura (ancho = e1). Regla de signo ESPEJO:
// cota positiva → pared IZQUIERDA a la derecha de la arista (x1 = arista + cota);
// cota negativa → pared DERECHA a la izquierda de la arista (x2 = arista + cota).
// Al cambiar el sentido, la cota se toma desde la otra pared (igual en SolidWorks).
function grvX1(g) {
    var xs = shaftBounds();
    var xe = xs[Math.max(0, Math.min(g.edge, xs.length - 1))];
    return g.off < 0 ? xe + g.off - g.e1 : xe + g.off;
}
// Ø del (único) nivel donde cae la ranura; 0 si pisa niveles con Ø distinto
function grvDiam(g) {
    var x1 = grvX1(g), x2 = x1 + g.e1, xs = shaftBounds(), d = 0;
    if (!isFinite(x1) || !isFinite(x2)) return 0;
    for (var i = 0; i < shaftLevels.length; i++) {
        if (xs[i + 1] <= x1 + KTOL || xs[i] >= x2 - KTOL) continue;
        if (!d) d = shaftLevels[i].d;
        else if (Math.abs(shaftLevels[i].d - d) >= 1e-9) return 0;
    }
    return d;
}
// null = válida; espejo de ShaftSpec.ValidateGroove del host
function validateGrv(g) {
    if (!(g.e1 > 0) || !(g.d3 > 0)) return 'El ancho E1 y el Ø de fondo D3 deben ser > 0.';
    if (!(g.edge >= 0) || !(g.edge <= shaftLevels.length)) return 'Arista de referencia no válida (¿cambiaste los niveles?).';
    if (!(Math.abs(g.off) > KTOL)) return 'La cota de posición no puede ser 0.';
    var xs = shaftBounds(), total = shaftTotal();
    var x1 = grvX1(g), x2 = x1 + g.e1;
    if (!(x1 > KTOL) || !(x2 < total - KTOL)) return 'La ranura se sale del eje (o toca un extremo).';
    for (var i = 1; i < shaftLevels.length; i++) {
        if (Math.abs(shaftLevels[i - 1].d - shaftLevels[i].d) < 1e-9) continue; // línea de división, no es pared
        var xc = xs[i];
        if (x1 <= xc + KTOL && x2 >= xc - KTOL) return 'La ranura cruza o toca un cambio de nivel (x = ' + fmt(xc) + ').';
    }
    var d = grvDiam(g);
    if (!(d > 0)) return 'La ranura debe caber entera en un nivel.';
    if (!(g.d3 < d)) return 'El Ø de fondo D3 debe ser menor que el Ø del nivel (' + fmt(d) + ').';
    return null;
}
// primera ranura aceptada que dejó de ser válida; null = todas bien
function shaftFirstBadGrv() {
    for (var i = 0; i < shaftGrvs.length; i++) {
        var msg = validateGrv(shaftGrvs[i]);
        if (msg !== null) return 'Ranura ' + (i + 1) + ': ' + msg;
    }
    return null;
}

// D3 cuando no hay fila DIN 471 para el Ø del nivel: Ø − 1 mm
function dinD3Fallback(d) { return d > 1 ? d - 1 : d * 0.9; }

// Re-aplica DIN 471 a las ranuras aceptadas con el check activado: si el Ø del
// nivel donde caen cambió (paso 1 o al moverlas), E1/D3 se actualizan solos.
function refreshGrvsDin() {
    for (var i = 0; i < shaftGrvs.length; i++) {
        var g = shaftGrvs[i];
        if (!g.din) continue;
        var d = grvDiam(g);
        if (!(d > 0)) continue;
        var row = dinLookup(d);
        if (row) { g.e1 = row.m; g.d3 = row.d3; }
        else { g.d3 = dinD3Fallback(d); }
    }
}

btnAddGrv.addEventListener('click', function () {
    // arranca centrada en el eje visible; E1/D3 desde DIN 471 si hay fila
    var g = { e1: 1.3, d3: 0, edge: 0, off: Math.max(1, shaftTotal() / 2 - 0.65), din: true };
    var d = grvDiam(g);
    var row = d > 0 ? dinLookup(d) : null;
    if (row) { g.e1 = row.m; g.d3 = row.d3; }
    else if (d > 0) { g.d3 = dinD3Fallback(d); }
    editGrv = g; editGrvIdx = -1;
    openGrvForm(); shaftUpdate();
});
btnGrvOk.addEventListener('click', function () {
    if (!editGrv || validateGrv(editGrv) !== null) return;
    if (editGrvIdx >= 0) shaftGrvs[editGrvIdx] = editGrv; else shaftGrvs.push(editGrv);
    closeGrvForm(); shaftUpdate();
});
btnGrvDel.addEventListener('click', function () {
    if (editGrvIdx >= 0) shaftGrvs.splice(editGrvIdx, 1);
    closeGrvForm(); shaftUpdate();
});
gIds.forEach(function (id) {
    gEl[id].addEventListener('input', function () {
        if (!editGrv) return;
        var v = parseFloat(gEl[id].value);
        if (id === 'goff') { editGrv.off = v; applyGrvDin(); } // moverla puede cambiar de nivel
        else if (id === 'ge1') editGrv.e1 = v;
        else if (id === 'gd3') editGrv.d3 = v;
        shaftUpdate();
    });
});
gEdgeSel.addEventListener('change', function () {
    if (!editGrv) return;
    editGrv.edge = parseInt(gEdgeSel.value, 10) || 0;
    editGrv.off = 3; gEl.goff.value = 3;        // default al elegir arista
    applyGrvDin(); shaftUpdate();
});

// autorrelleno DIN 471 con el Ø del nivel donde cae la ranura ahora mismo
function applyGrvDin() {
    if (!editGrv) return;
    var on = gDinChk.checked;
    editGrv.din = on;                       // persiste: al cambiar el eje se re-aplica solo
    gEl.ge1.disabled = on; gEl.gd3.disabled = on;
    if (!on) { gDinNote.textContent = ''; gDinNote.className = 'note'; return; }
    var d = grvDiam(editGrv);
    var row = d > 0 ? dinLookup(d) : null;
    if (row) {
        editGrv.e1 = row.m; editGrv.d3 = row.d3;
        gEl.ge1.value = row.m; gEl.gd3.value = row.d3;
        gDinNote.textContent = 'DIN 471 d=' + fmt(d) + ': E1=' + fmt(row.m) + ' · D3=' + fmt(row.d3);
        gDinNote.className = 'note';
    } else if (d > 0) {
        editGrv.d3 = dinD3Fallback(d);
        gEl.gd3.value = editGrv.d3;
        gEl.ge1.disabled = false;
        gDinNote.textContent = 'Sin fila DIN 471 para Ø' + fmt(d) + ' → D3 = Ø−1 = ' + fmt(editGrv.d3) + ' · E1 manual.';
        gDinNote.className = 'note warn';
    } else {
        gEl.ge1.disabled = false; gEl.gd3.disabled = false;
        gDinNote.textContent = 'La ranura no cae en un único nivel — colócala primero.';
        gDinNote.className = 'note warn';
    }
}
gDinChk.addEventListener('change', function () { applyGrvDin(); shaftUpdate(); });

function openGrvForm() {
    gEl.ge1.value = editGrv.e1; gEl.gd3.value = editGrv.d3; gEl.goff.value = editGrv.off;
    var xs = shaftBounds(), opts = '';
    for (var i = 0; i < xs.length; i++) {
        var label = i === 0 ? 'Extremo izquierdo (x = 0)'
            : i === xs.length - 1 ? 'Extremo derecho (x = ' + fmt(xs[i]) + ')'
                : 'Cambio nivel ' + i + '→' + (i + 1) + ' (x = ' + fmt(xs[i]) + ')';
        opts += '<option value="' + i + '">' + label + '</option>';
    }
    gEdgeSel.innerHTML = opts;
    gEdgeSel.value = String(editGrv.edge);
    gDinChk.checked = editGrv.din !== false; applyGrvDin();
    grvFormEl.classList.remove('hidden');
    btnAddGrv.classList.add('hidden');
    btnGrvDel.style.display = editGrvIdx >= 0 ? '' : 'none';
    renderGrvList();
}
function closeGrvForm() {
    editGrv = null; editGrvIdx = -1;
    grvFormEl.classList.add('hidden');
    btnAddGrv.classList.remove('hidden');
    renderGrvList();
}
function renderGrvList() {
    var html = '';
    for (var i = 0; i < shaftGrvs.length; i++) {
        var g = shaftGrvs[i], x1 = grvX1(g);
        var pos = isFinite(x1) ? fmt(x1) + '…' + fmt(x1 + g.e1) : '—';
        html += '<div class="keyitem' + (i === editGrvIdx ? ' editing' : '') + '" data-grv="' + i + '">' +
            '<span>' + (i + 1) + ' · E1 ' + fmt(g.e1) + ' × D3 ' + fmt(g.d3) + ' · x ' + pos + '</span>' +
            '<span class="kx" data-gdel="' + i + '">✕</span></div>';
    }
    grvListEl.innerHTML = html;
    var items = grvListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-gdel');
            if (del !== null && del !== undefined) {
                shaftGrvs.splice(parseInt(del, 10), 1);
                closeGrvForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editGrvIdx = parseInt(this.getAttribute('data-grv'), 10);
            editGrv = JSON.parse(JSON.stringify(shaftGrvs[editGrvIdx]));
            openGrvForm(); shaftUpdate();
        });
    }
}

// ---- paso 4: entalladuras DIN 509-E --------------------------------------

// hombros = fronteras con cambio de Ø (una línea de división no es un hombro)
function shoulderList() {
    var xs = shaftBounds(), out = [];
    for (var i = 1; i < shaftLevels.length; i++) {
        var dL = shaftLevels[i - 1].d, dR = shaftLevels[i].d;
        if (!(dL > 0) || !(dR > 0) || Math.abs(dL - dR) < 1e-9) continue;
        out.push({ bnd: i, x: xs[i], dL: dL, dR: dR, dSmall: Math.min(dL, dR), h: Math.abs(dL - dR) / 2 });
    }
    return out;
}
function shoulderInfo(bnd) {
    var sh = shoulderList();
    for (var i = 0; i < sh.length; i++) if (sh[i].bnd === bnd) return sh[i];
    return null;
}
function ucSmallLeft(u) { return shaftLevels[u.bnd - 1].d < shaftLevels[u.bnd].d; }
// zona axial [z1, z2] que ocupa la entalladura + Ø de la superficie tallada
function ucZone(u) {
    var xs = shaftBounds(), x = xs[u.bnd];
    return ucSmallLeft(u)
        ? [x - u.f, x, shaftLevels[u.bnd - 1].d]
        : [x, x + u.f, shaftLevels[u.bnd].d];
}
// tramo continuo [ini, fin] de la superficie menor (niveles de igual Ø fundidos)
function ucSegment(u) {
    var xs = shaftBounds();
    var idx = ucSmallLeft(u) ? u.bnd - 1 : u.bnd;
    var d = shaftLevels[idx].d, first = idx, last = idx;
    while (first > 0 && Math.abs(shaftLevels[first - 1].d - d) < 1e-9) first--;
    while (last < shaftLevels.length - 1 && Math.abs(shaftLevels[last + 1].d - d) < 1e-9) last++;
    return [xs[first], xs[last + 1]];
}
// fila DIN 509 para el Ø menor y la solicitación ('usual' | 'fatiga'); null si no hay
// (la serie fatiga solo existe para Ø > 18)
function din509Row(d, ser) {
    for (var i = 0; i < DIN509.length; i++) {
        var o = DIN509[i];
        if (o.ser === ser && d > o.min && d <= o.max + 1e-9) return o;
    }
    return null;
}
function serLabel(ser) { return ser === 'fatiga' ? 'fatiga aumentada' : 'esfuerzo usual'; }
// zona REAL que ocupa el corte: la de ucZone, extendida t2 hacia el lado mayor si es F
function ucSpan(u) {
    var z = ucZone(u);
    if (u.form === 'F' && u.t2 > 0) {
        if (ucSmallLeft(u)) z = [z[0], z[1] + u.t2, z[2]];
        else z = [z[0] - u.t2, z[1], z[2]];
    }
    return z;
}
// null = válida; espejo de ShaftSpec.ValidateUndercut del host
function validateUc(u, idx) {
    if (!(u.bnd >= 1) || !(u.bnd <= shaftLevels.length - 1)) return 'Hombro no válido (¿cambiaste los niveles?).';
    var dL = shaftLevels[u.bnd - 1].d, dR = shaftLevels[u.bnd].d;
    if (!(dL > 0) || !(dR > 0)) return 'Los Ø del hombro deben ser mayores que 0.';
    if (Math.abs(dL - dR) < 1e-9) return 'La frontera elegida ya no es un hombro (Ø iguales).';
    if (u.form !== 'E' && u.form !== 'F') return 'Elige el tipo (E o F).';
    if (!(u.r > 0) || !(u.t1 > 0) || !(u.f > 0)) return 'No hay medida DIN 509 para ese Ø y solicitación.';
    if (u.form === 'F' && !(u.t2 > 0)) return 'La forma F necesita t2 mayor que 0.';
    // E: la tangencia del redondeo sube hasta r − t1 sobre la superficie menor.
    // F: la salida a 8° por la cara llega a max(r, t2/tan 8°) − t1. El hombro debe superarlo.
    var h = Math.abs(dL - dR) / 2;
    var top = (u.form === 'F' ? Math.max(u.r, u.t2 * UC_RAMP8) : u.r) - u.t1;
    if (!(h > top + KTOL)) {
        return 'El hombro es demasiado bajo para ' + u.form + ' ' + u.r + '×' + u.t1 +
            ' (altura ' + fmt(h) + ' ≤ ' + fmt(top) + ' mm).';
    }
    // un extremo de la zona ES el hombro (= frontera del tramo); solo el extremo
    // lejano (la salida a 15°) debe quedar estrictamente dentro del tramo
    var z = ucZone(u), seg = ucSegment(u);
    var fits = ucSmallLeft(u) ? (z[0] > seg[0] + KTOL) : (z[1] < seg[1] - KTOL);
    if (!fits) {
        return 'El ancho f (' + fmt(u.f) + ' mm) no cabe en el tramo de Ø' + fmt(z[2]) + '.';
    }
    // la F asoma t2 al otro lado del hombro: el tramo del Ø mayor debe ser más largo
    if (u.form === 'F') {
        var xs = shaftBounds(), xsh = xs[u.bnd];
        var bigIdx = ucSmallLeft(u) ? u.bnd : u.bnd - 1;
        var dBig = shaftLevels[bigIdx].d, fb = bigIdx, lb = bigIdx;
        while (fb > 0 && Math.abs(shaftLevels[fb - 1].d - dBig) < 1e-9) fb--;
        while (lb < shaftLevels.length - 1 && Math.abs(shaftLevels[lb + 1].d - dBig) < 1e-9) lb++;
        var room = ucSmallLeft(u) ? xs[lb + 1] - xsh : xsh - xs[fb];
        if (!(room > u.t2 + KTOL)) {
            return 'La profundidad t2 (' + fmt(u.t2) + ' mm) no cabe en el tramo de Ø' + fmt(dBig) + '.';
        }
    }
    var sp = ucSpan(u);
    for (var i = 0; i < shaftUcs.length; i++) {
        if (i === idx) continue;
        var o = shaftUcs[i];
        if (o.bnd === u.bnd) return 'Ya hay otra entalladura en ese hombro.';
        if (!(o.bnd >= 1) || !(o.bnd <= shaftLevels.length - 1)) continue;
        var oz = ucSpan(o);
        if (sp[0] < oz[1] + KTOL && sp[1] > oz[0] - KTOL) return 'Se solapa con la entalladura ' + (i + 1) + '.';
    }
    for (var g = 0; g < shaftGrvs.length; g++) {
        var g1 = grvX1(shaftGrvs[g]), g2 = g1 + shaftGrvs[g].e1;
        if (isFinite(g1) && sp[0] < g2 + KTOL && sp[1] > g1 - KTOL) return 'Se solapa con la ranura de anillo ' + (g + 1) + '.';
    }
    for (var k = 0; k < shaftKeys.length; k++) {
        var k1 = keyX1(shaftKeys[k]), k2 = k1 + keySpan(shaftKeys[k]);
        if (isFinite(k1) && sp[0] < k2 + KTOL && sp[1] > k1 - KTOL) return 'Se solapa con la chaveta ' + (k + 1) + '.';
    }
    return null;
}
// primera entalladura aceptada que dejó de ser válida; null = todas bien
function shaftFirstBadUc() {
    for (var i = 0; i < shaftUcs.length; i++) {
        var msg = validateUc(shaftUcs[i], i);
        if (msg !== null) return 'Entalladura ' + (i + 1) + ': ' + msg;
    }
    return null;
}

btnAddUc.addEventListener('click', function () {
    var sh = shoulderList();
    // primer hombro libre (sin entalladura); si no hay libres, el primero
    var bnd = sh.length ? sh[0].bnd : 0;
    for (var i = 0; i < sh.length; i++) {
        var taken = false;
        for (var j = 0; j < shaftUcs.length; j++) if (shaftUcs[j].bnd === sh[i].bnd) taken = true;
        if (!taken) { bnd = sh[i].bnd; break; }
    }
    editUc = { bnd: bnd, form: 'E', ser: 'usual', r: 0, t1: 0, f: 0, t2: 0 };
    editUcIdx = -1;
    openUcForm(); shaftUpdate();
});
btnUcOk.addEventListener('click', function () {
    if (!editUc || validateUc(editUc, editUcIdx) !== null) return;
    if (editUcIdx >= 0) shaftUcs[editUcIdx] = editUc; else shaftUcs.push(editUc);
    closeUcForm(); shaftUpdate();
});
btnUcDel.addEventListener('click', function () {
    if (editUcIdx >= 0) shaftUcs.splice(editUcIdx, 1);
    closeUcForm(); shaftUpdate();
});
uEdgeSel.addEventListener('change', function () {
    if (!editUc) return;
    editUc.bnd = parseInt(uEdgeSel.value, 10) || 0;
    syncUcSize(); shaftUpdate();
});
ucTypeSel.addEventListener('change', function () {
    if (!editUc) return;
    editUc.form = ucTypeSel.value === 'F' ? 'F' : 'E';
    syncUcSize(); shaftUpdate();
});
ucSerieSel.addEventListener('change', function () {
    if (!editUc) return;
    editUc.ser = ucSerieSel.value === 'fatiga' ? 'fatiga' : 'usual';
    syncUcSize(); shaftUpdate();
});

// medida DIN 509 del hombro actual: única fila según Ø menor + solicitación; sin fila
// (p. ej. fatiga con Ø ≤ 18) se anulan r/t1/f y validateUc bloquea el Aceptar
function syncUcSize() {
    if (!editUc) return;
    var sh = shoulderInfo(editUc.bnd);
    var row = sh ? din509Row(sh.dSmall, editUc.ser) : null;
    if (!row) {
        editUc.r = 0; editUc.t1 = 0; editUc.f = 0; editUc.t2 = 0;
        ucNoteEl.textContent = sh
            ? 'Sin medida DIN 509 para Ø' + fmt(sh.dSmall) + ' con ' + serLabel(editUc.ser) +
              (editUc.ser === 'fatiga' ? ' (la serie fatiga empieza en Ø > 18).' : '.')
            : 'El eje no tiene hombros (ningún cambio de Ø).';
        ucNoteEl.className = 'note warn';
        return;
    }
    editUc.r = row.r; editUc.t1 = row.t1; editUc.f = row.f;
    editUc.t2 = editUc.form === 'F' ? row.t2 : 0;
    ucNoteEl.textContent = 'DIN 509 – ' + editUc.form + ' ' + row.r + '×' + row.t1 +
        ' · f ' + row.f + ' mm' +
        (editUc.form === 'F' ? ' · t2 ' + row.t2 + ' mm' : '') +
        ' · fondo Ø' + fmt(sh.dSmall - 2 * row.t1);
    ucNoteEl.className = 'note';
}

function openUcForm() {
    var sh = shoulderList(), opts = '';
    for (var i = 0; i < sh.length; i++) {
        opts += '<option value="' + sh[i].bnd + '">Hombro ' + sh[i].bnd + '→' + (sh[i].bnd + 1) +
            ': Ø' + fmt(sh[i].dL) + ' → Ø' + fmt(sh[i].dR) + ' (x = ' + fmt(sh[i].x) + ')</option>';
    }
    uEdgeSel.innerHTML = opts;
    uEdgeSel.disabled = sh.length === 0;
    if (sh.length) uEdgeSel.value = String(editUc.bnd);
    ucTypeSel.value = editUc.form === 'F' ? 'F' : 'E';
    ucSerieSel.value = editUc.ser === 'fatiga' ? 'fatiga' : 'usual';
    syncUcSize();
    ucFormEl.classList.remove('hidden');
    btnAddUc.classList.add('hidden');
    btnUcDel.style.display = editUcIdx >= 0 ? '' : 'none';
    renderUcList();
}
function closeUcForm() {
    editUc = null; editUcIdx = -1;
    ucFormEl.classList.add('hidden');
    btnAddUc.classList.remove('hidden');
    renderUcList();
}
function renderUcList() {
    var html = '';
    for (var i = 0; i < shaftUcs.length; i++) {
        var u = shaftUcs[i];
        var sh = shoulderInfo(u.bnd);
        html += '<div class="keyitem' + (i === editUcIdx ? ' editing' : '') + '" data-uc="' + i + '">' +
            '<span>' + (i + 1) + ' · ' + u.form + ' ' + u.r + '×' + u.t1 + ' · ' + serLabel(u.ser) +
            (sh ? ' · hombro x ' + fmt(sh.x) + ' · Ø' + fmt(sh.dSmall) : ' · hombro ?') + '</span>' +
            '<span class="kx" data-udel="' + i + '">✕</span></div>';
    }
    ucListEl.innerHTML = html;
    var items = ucListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-udel');
            if (del !== null && del !== undefined) {
                shaftUcs.splice(parseInt(del, 10), 1);
                closeUcForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editUcIdx = parseInt(this.getAttribute('data-uc'), 10);
            editUc = JSON.parse(JSON.stringify(shaftUcs[editUcIdx]));
            openUcForm(); shaftUpdate();
        });
    }
}

// ---- paso 5: puntos de centrado (DIN 332) --------------------------------

// Ø del extremo donde va el punto (nivel 0 = izquierdo, último = derecho)
function chEndDiam(end) { return end === 0 ? shaftLevels[0].d : shaftLevels[shaftLevels.length - 1].d; }
function chTable(form) { return form === 'D' ? DIN332_2 : DIN332_1; }
function chRowKey(form, row) { return form === 'D' ? row.m : row.d1; }
function chRecommend(form, d) {
    var t = chTable(form);
    for (var i = 0; i < t.length; i++) if (d > t[i].min && d <= t[i].max + 1e-9) return t[i];
    return t[t.length - 1];
}
// avance axial del flanco de radio (forma R): √(r² − (r + d1/2 − d2/2)²); 0 si el radio no cierra
function chArcFlank(c) {
    var r1 = c.d1 / 2, r2 = c.d2 / 2, R = c.rarc, a = R + r1 - r2, disc = R * R - a * a;
    return disc > 0 ? Math.sqrt(disc) : 0;
}
// profundidad total del taladro (mm), espejo de ShaftCenterHole.TotalDepthMm del host
function chDepth(c) {
    var r1 = c.d1 / 2, r2 = c.d2 / 2, r3 = (c.d3 || 0) / 2, tip = r1 / CH_TAN60;
    if (c.form === 'D') { var hp = r3 > r2 ? (r3 - r2) / CH_TAN60 : 0; return hp + c.lc + c.lp + tip; }
    if (c.form === 'R') return chArcFlank(c) + c.lp + tip;
    var hpB = (c.form === 'B' && r3 > r2) ? (r3 - r2) / CH_TAN60 : 0;
    var hc = (r2 - r1) / CH_TAN30;
    return hpB + hc + c.lp + tip;
}
function chMouth(c) { return Math.max(c.d2, c.d3 || 0); }

// rellena las medidas de c con la fila elegida según la forma
function applyChRow(row) {
    if (!editCh) return;
    var f = editCh.form;
    editCh._key = chRowKey(f, row);
    editCh.d1 = row.d1;
    if (f === 'D') {
        editCh.d2 = row.d2; editCh.d3 = row.d3; editCh.thread = row.m;
        editCh.rarc = 0; editCh.lc = chCbLen(row.d1); editCh.lp = chBoreLen(row.m);
    } else if (f === 'R') {
        editCh.d2 = row.d2; editCh.d3 = 0; editCh.thread = 0;
        editCh.rarc = row.r; editCh.lc = 0; editCh.lp = chPilotLen(row.d1);
    } else { // A o B
        editCh.d2 = row.d2; editCh.d3 = f === 'B' ? row.d3 : 0; editCh.thread = 0;
        editCh.rarc = 0; editCh.lc = 0; editCh.lp = chPilotLen(row.d1);
    }
    updateChNote();
}
// (re)construye el combo de tamaños; conserva la selección si sigue existiendo, si no la recomendada
function syncChSize() {
    if (!editCh) return;
    var form = editCh.form, t = chTable(form), d = chEndDiam(editCh.end), opts = '';
    for (var i = 0; i < t.length; i++) {
        var key = chRowKey(form, t[i]);
        var lbl = form === 'D'
            ? ('M' + key + ' · broca Ø' + fmt(t[i].d1) + ' · protección Ø' + fmt(t[i].d3))
            : ('d1 Ø' + fmt(t[i].d1) + ' · boca Ø' + fmt(form === 'B' ? t[i].d3 : t[i].d2));
        opts += '<option value="' + key + '">' + lbl + '</option>';
    }
    csizeSel.innerHTML = opts;
    var found = null;
    for (var j = 0; j < t.length; j++) if (Math.abs(chRowKey(form, t[j]) - editCh._key) < 1e-9) found = t[j];
    if (!found) found = chRecommend(form, d);
    csizeSel.value = String(chRowKey(form, found));
    applyChRow(found);
}
function updateChNote() {
    if (!editCh) return;
    var c = editCh;
    var body = c.form === 'D'
        ? ('M' + c.thread + ' · broca Ø' + fmt(c.d1) + ' · rebaje Ø' + fmt(c.d2) + ' · protección Ø' + fmt(c.d3))
        : ('d1 Ø' + fmt(c.d1) + ' · boca Ø' + fmt(c.form === 'B' ? c.d3 : c.d2) + (c.form === 'R' ? ' · r ' + fmt(c.rarc) : ''));
    chNoteEl.textContent = 'DIN 332-' + (c.form === 'D' ? '2' : '1') + ' ' + c.form + ' · ' + body +
        ' · prof. ' + fmt(chDepth(c)) + ' mm';
    chNoteEl.className = 'note';
}

// null = válido; espejo de ShaftSpec.ValidateCenterHole del host
function validateCh(c, idx) {
    if (!(c.end === 0 || c.end === 1)) return 'Extremo no válido.';
    if (c.form !== 'A' && c.form !== 'B' && c.form !== 'R' && c.form !== 'D') return 'Elige el tipo (A, B, R o D).';
    if (!(c.d1 > 0) || !(c.d2 > c.d1)) return 'No hay medida DIN 332 para ese tamaño.';
    if (!(c.lp > 0)) return 'La profundidad del taladro debe ser > 0.';
    if ((c.form === 'B' || c.form === 'D') && !(c.d3 > c.d2)) return 'El avellanado de protección d3 debe ser mayor que d2.';
    if (c.form === 'R' && (!(c.rarc > 0) || !(chArcFlank(c) > 0))) return 'El radio r de la forma R es demasiado pequeño.';
    if (c.form === 'D') {
        if (!(c.lc > 0)) return 'La longitud del rebaje debe ser > 0.';
        if (!(c.thread > c.d1)) return 'El Ø nominal de la rosca debe ser mayor que la broca d1.';
    }
    var lv = c.end === 0 ? shaftLevels[0] : shaftLevels[shaftLevels.length - 1];
    if (!(lv.d > 0) || !(lv.l > 0)) return 'El nivel del extremo no es válido.';
    if (!(chMouth(c) < lv.d)) return 'No cabe en la cara del extremo (Ø boca ' + fmt(chMouth(c)) + ' ≥ Ø' + fmt(lv.d) + ').';
    var depth = chDepth(c);
    if (!(depth < lv.l - KTOL)) return 'Es más profundo que el nivel del extremo (' + fmt(depth) + ' ≥ ' + fmt(lv.l) + ' mm).';
    for (var i = 0; i < shaftChs.length; i++) {
        if (i !== idx && shaftChs[i].end === c.end) return 'Ya hay otro punto de centrado en ese extremo.';
    }
    return null;
}
function shaftFirstBadCh() {
    for (var i = 0; i < shaftChs.length; i++) {
        var m = validateCh(shaftChs[i], i);
        if (m !== null) return 'Punto de centrado ' + (i + 1) + ': ' + m;
    }
    return null;
}

btnAddCh.addEventListener('click', function () {
    var used = {};
    for (var i = 0; i < shaftChs.length; i++) used[shaftChs[i].end] = true;
    var end = used[0] ? (used[1] ? 0 : 1) : 0;
    editCh = { end: end, form: 'A', _key: 0, d1: 0, d2: 0, d3: 0, lp: 0, rarc: 0, lc: 0, thread: 0 };
    editChIdx = -1;
    openChForm(); shaftUpdate();
});
btnChOk.addEventListener('click', function () {
    if (!editCh || validateCh(editCh, editChIdx) !== null) return;
    if (editChIdx >= 0) shaftChs[editChIdx] = editCh; else shaftChs.push(editCh);
    closeChForm(); shaftUpdate();
});
btnChDel.addEventListener('click', function () {
    if (editChIdx >= 0) shaftChs.splice(editChIdx, 1);
    closeChForm(); shaftUpdate();
});
cEndSel.addEventListener('change', function () {
    if (!editCh) return;
    editCh.end = parseInt(cEndSel.value, 10) || 0;
    syncChSize(); shaftUpdate();
});
cTypeSel.addEventListener('change', function () {
    if (!editCh) return;
    editCh.form = cTypeSel.value;
    syncChSize(); shaftUpdate();
});
csizeSel.addEventListener('change', function () {
    if (!editCh) return;
    var t = chTable(editCh.form), key = parseFloat(csizeSel.value);
    for (var i = 0; i < t.length; i++) {
        if (Math.abs(chRowKey(editCh.form, t[i]) - key) < 1e-9) { applyChRow(t[i]); break; }
    }
    shaftUpdate();
});

function openChForm() {
    cEndSel.value = String(editCh.end);
    cTypeSel.value = editCh.form;
    syncChSize();
    chFormEl.classList.remove('hidden');
    btnAddCh.classList.add('hidden');
    btnChDel.style.display = editChIdx >= 0 ? '' : 'none';
    renderChList();
}
function closeChForm() {
    editCh = null; editChIdx = -1;
    chFormEl.classList.add('hidden');
    btnAddCh.classList.remove('hidden');
    renderChList();
}
function renderChList() {
    var html = '';
    for (var i = 0; i < shaftChs.length; i++) {
        var c = shaftChs[i];
        var size = c.form === 'D' ? 'M' + c.thread : 'd1 ' + fmt(c.d1);
        html += '<div class="keyitem' + (i === editChIdx ? ' editing' : '') + '" data-ch="' + i + '">' +
            '<span>' + (i + 1) + ' · ' + c.form + ' ' + size + ' · ' +
            (c.end === 0 ? 'extremo izq.' : 'extremo der.') + ' · prof. ' + fmt(chDepth(c)) + '</span>' +
            '<span class="kx" data-cdel="' + i + '">✕</span></div>';
    }
    chListEl.innerHTML = html;
    var items = chListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-cdel');
            if (del !== null && del !== undefined) {
                shaftChs.splice(parseInt(del, 10), 1);
                closeChForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editChIdx = parseInt(this.getAttribute('data-ch'), 10);
            editCh = JSON.parse(JSON.stringify(shaftChs[editChIdx]));
            openChForm(); shaftUpdate();
        });
    }
}
// puntos del semiperfil superior (boca → punta), x absoluto en mm — para el dibujo del alzado
function chProfilePts(c) {
    var total = shaftTotal(), xf = c.end === 0 ? 0 : total, sign = c.end === 0 ? 1 : -1;
    var r1 = c.d1 / 2, r2 = c.d2 / 2, r3 = (c.d3 || 0) / 2, tip = r1 / CH_TAN60, pts = [];
    function push(p, r) { pts.push({ x: xf + sign * p, r: r }); }
    if (c.form === 'D') {
        var hp = r3 > r2 ? (r3 - r2) / CH_TAN60 : 0;
        push(0, r3); push(hp, r2); push(hp + c.lc, r2); push(hp + c.lc, r1);
        push(hp + c.lc + c.lp, r1); push(hp + c.lc + c.lp + tip, 0);
    } else if (c.form === 'R') {
        var hR = chArcFlank(c), cx = hR, cy = r1 + c.rarc;
        var a0 = Math.atan2(r2 - cy, 0 - cx), a1 = Math.atan2(r1 - cy, hR - cx), N = 8;
        for (var i = 0; i <= N; i++) { var a = a0 + (a1 - a0) * i / N; push(cx + c.rarc * Math.cos(a), cy + c.rarc * Math.sin(a)); }
        push(hR + c.lp, r1); push(hR + c.lp + tip, 0);
    } else {
        var hpB = (c.form === 'B' && r3 > r2) ? (r3 - r2) / CH_TAN60 : 0, hc = (r2 - r1) / CH_TAN30;
        if (c.form === 'B') push(0, r3);
        push(hpB, r2); push(hpB + hc, r1); push(hpB + hc + c.lp, r1); push(hpB + hc + c.lp + tip, 0);
    }
    return pts;
}

// ---- render / estado ------------------------------------------------------

function shaftRender() {
    document.getElementById('sgrp1').classList.toggle('hidden', shaftStep !== 1);
    document.getElementById('sgrp2').classList.toggle('hidden', shaftStep !== 2);
    document.getElementById('sgrp3').classList.toggle('hidden', shaftStep !== 3);
    document.getElementById('sgrp4').classList.toggle('hidden', shaftStep !== 4);
    document.getElementById('sgrp5').classList.toggle('hidden', shaftStep !== 5);
    document.getElementById('sdot1').className = 'dot ' + (shaftStep === 1 ? 'active' : 'done');
    document.getElementById('sdot2').className = 'dot ' + (shaftStep === 2 ? 'active' : shaftStep > 2 ? 'done' : '');
    document.getElementById('sdot3').className = 'dot ' + (shaftStep === 3 ? 'active' : shaftStep > 3 ? 'done' : '');
    document.getElementById('sdot4').className = 'dot ' + (shaftStep === 4 ? 'active' : shaftStep > 4 ? 'done' : '');
    document.getElementById('sdot5').className = 'dot ' + (shaftStep === 5 ? 'active' : '');
    shaftBackBtn.textContent = shaftStep === 1 ? 'Volver' : 'Atrás';
    shaftNextBtn.textContent = shaftStep === 5 ? 'Crear' : 'Siguiente';
    shaftSubEl.textContent = shaftStep === 1
        ? 'Paso 1 de 5 · niveles de izquierda a derecha · un nivel = Ø × L'
        : shaftStep === 2
            ? 'Paso 2 de 5 · chavetas (forma A) · elige arista, cota ±, profundidad, ángulo y nº'
            : shaftStep === 3
                ? 'Paso 3 de 5 · ranuras de anillo (DIN 471) · elige arista, cota ±, E1 y D3'
                : shaftStep === 4
                    ? 'Paso 4 de 5 · entalladuras (DIN 509) · elige hombro, tipo y solicitación'
                    : 'Paso 5 de 5 · puntos de centrado (DIN 332) · elige extremo, tipo y tamaño';
    if (shaftStep === 2) renderKeyList();
    if (shaftStep === 3) renderGrvList();
    if (shaftStep === 4) renderUcList();
    if (shaftStep === 5) renderChList();
    shaftUpdate();
}

function shaftUpdate() {
    refreshGrvsDin();       // el Ø de un nivel pudo cambiar → E1/D3 de las ranuras DIN siguen al eje
    shaftErrEl.className = 'err'; shaftErrEl.textContent = '';
    if (shaftStep === 1) {
        var reason = shaftValidate();
        if (reason) { shaftErrEl.textContent = reason; }
        else {
            shaftErrEl.className = 'err ok';
            shaftErrEl.textContent = shaftLevels.length + ' nivel(es) · longitud total ' + fmt(shaftTotal()) + ' mm';
        }
        shaftNextBtn.disabled = reason !== null;
        var splits = reason === null ? shaftSplits() : [];
        shaftSplitNoteEl.textContent = splits.length
            ? 'Ø iguales consecutivos → línea de división en x = ' + splits.map(fmt).join(', ') + ' mm'
            : '';
    } else if (shaftStep === 2) {
        if (editKey) {
            var kmsg = validateKey(editKey);
            btnKeyOk.disabled = kmsg !== null;
            shaftNextBtn.disabled = true;
            if (kmsg) { shaftErrEl.textContent = kmsg; }
            else {
                var x1 = keyX1(editKey);
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Chaveta x ' + fmt(x1) + '…' + fmt(x1 + keySpan(editKey)) +
                    ' mm · fondo Ø' + fmt(editKey.refd - 2 * editKey.depth);
            }
            drawKeySection();
        } else {
            var bad = shaftFirstBadKey();
            if (bad) { shaftErrEl.textContent = bad + ' (edítala o bórrala)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftKeys.length + ' chaveta(s) · siguiente: ranuras de anillo';
            }
            shaftNextBtn.disabled = bad !== null;
        }
    } else if (shaftStep === 3) {
        if (editGrv) {
            var gmsg = validateGrv(editGrv);
            btnGrvOk.disabled = gmsg !== null;
            shaftNextBtn.disabled = true;
            if (gmsg) { shaftErrEl.textContent = gmsg; }
            else {
                var gx1 = grvX1(editGrv);
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Ranura x ' + fmt(gx1) + '…' + fmt(gx1 + editGrv.e1) +
                    ' mm · fondo Ø' + fmt(editGrv.d3);
            }
        } else {
            var gbad = shaftFirstBadGrv();
            if (gbad) { shaftErrEl.textContent = gbad + ' (edítala o bórrala)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftGrvs.length + ' ranura(s) · siguiente: entalladuras';
            }
            shaftNextBtn.disabled = gbad !== null;
        }
    } else if (shaftStep === 4) {
        if (editUc) {
            var umsg = validateUc(editUc, editUcIdx);
            btnUcOk.disabled = umsg !== null;
            shaftNextBtn.disabled = true;
            if (umsg) { shaftErrEl.textContent = umsg; }
            else {
                var uz = ucSpan(editUc);
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Entalladura ' + editUc.form + ' ' + editUc.r + '×' + editUc.t1 +
                    (editUc.form === 'F' ? ' · t2 ' + editUc.t2 : '') +
                    ' · x ' + fmt(uz[0]) + '…' + fmt(uz[1]) + ' mm · fondo Ø' + fmt(uz[2] - 2 * editUc.t1);
            }
        } else {
            var ubad = shaftFirstBadUc();
            if (ubad) { shaftErrEl.textContent = ubad + ' (edítala o bórrala)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftUcs.length + ' entalladura(s) · siguiente: puntos de centrado';
            }
            shaftNextBtn.disabled = ubad !== null;
        }
    } else {
        if (editCh) {
            var cmsg = validateCh(editCh, editChIdx);
            btnChOk.disabled = cmsg !== null;
            shaftNextBtn.disabled = true;
            if (cmsg) { shaftErrEl.textContent = cmsg; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Punto ' + editCh.form + (editCh.form === 'D' ? ' M' + editCh.thread : ' d1 ' + fmt(editCh.d1)) +
                    ' · ' + (editCh.end === 0 ? 'extremo izq.' : 'extremo der.') +
                    ' · boca Ø' + fmt(chMouth(editCh)) + ' · prof. ' + fmt(chDepth(editCh)) + ' mm';
            }
        } else {
            var cbad = shaftFirstBadCh();
            if (cbad) { shaftErrEl.textContent = cbad + ' (edítalo o bórralo)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftChs.length + ' punto(s) de centrado · listo para crear';
            }
            shaftNextBtn.disabled = cbad !== null;
        }
    }
    drawShaft();
}

// ---- dibujo: alzado del eje ------------------------------------------------

// zoom/pan del alzado: se aplica al viewBox del SVG (la geometría no cambia; el grosor
// de línea se mantiene en pantalla vía vector-effect: non-scaling-stroke en el CSS).
// z = aumento (1 = encuadre completo), px/py = desplazamiento en unidades de viewBox.
// Rueda = zoom centrado en el cursor · arrastre con la rueda pulsada = pan · ⟲ = encuadre.
var SVBW = 460, SVBH = 320;
var shaftView = { z: 1, px: 0, py: 0 };
function shaftViewBox() {
    var vw = SVBW / shaftView.z, vh = SVBH / shaftView.z;
    return [(SVBW - vw) / 2 + shaftView.px, (SVBH - vh) / 2 + shaftView.py, vw, vh];
}
function shaftClampPan() {
    shaftView.px = Math.max(-SVBW / 2, Math.min(SVBW / 2, shaftView.px));
    shaftView.py = Math.max(-SVBH / 2, Math.min(SVBH / 2, shaftView.py));
}
function shaftZoomAction(a) {
    if (a === 'reset') { shaftView.z = 1; shaftView.px = 0; shaftView.py = 0; drawShaft(); }
}
// zoom con la rueda, anclado al punto del dibujo bajo el cursor (mapeo del
// preserveAspectRatio "meet" centrado: escala mínima + offsets de centrado)
shaftCanvasEl.addEventListener('wheel', function (e) {
    var svg = shaftCanvasEl.querySelector('svg');
    if (!svg) return;
    e.preventDefault();
    var r = svg.getBoundingClientRect();
    var vb = shaftViewBox();
    var s = Math.min(r.width / vb[2], r.height / vb[3]);
    var ox = (r.width - vb[2] * s) / 2, oy = (r.height - vb[3] * s) / 2;
    var wx = vb[0] + (e.clientX - r.left - ox) / s;   // punto bajo el cursor (unid. viewBox)
    var wy = vb[1] + (e.clientY - r.top - oy) / s;
    var nz = Math.max(1, Math.min(8, shaftView.z * (e.deltaY < 0 ? 1.2 : 1 / 1.2)));
    if (nz === shaftView.z) return;
    shaftView.z = nz;
    if (nz === 1) { shaftView.px = 0; shaftView.py = 0; }
    else {
        var vw = SVBW / nz, vh = SVBH / nz;
        var s2 = Math.min(r.width / vw, r.height / vh);
        var ox2 = (r.width - vw * s2) / 2, oy2 = (r.height - vh * s2) / 2;
        shaftView.px = wx - (e.clientX - r.left - ox2) / s2 - (SVBW - vw) / 2;
        shaftView.py = wy - (e.clientY - r.top - oy2) / s2 - (SVBH - vh) / 2;
        shaftClampPan();
    }
    drawShaft();
}, { passive: false });
// pan arrastrando con la rueda (botón central) pulsada
var shaftDrag = null;
shaftCanvasEl.addEventListener('pointerdown', function (e) {
    if (e.button !== 1) return;
    var svg = shaftCanvasEl.querySelector('svg');
    if (!svg) return;
    e.preventDefault();
    var r = svg.getBoundingClientRect();
    var vb = shaftViewBox();
    shaftDrag = {
        x: e.clientX, y: e.clientY, px: shaftView.px, py: shaftView.py,
        s: Math.min(r.width / vb[2], r.height / vb[3])
    };
    shaftCanvasEl.setPointerCapture(e.pointerId);
});
shaftCanvasEl.addEventListener('pointermove', function (e) {
    if (!shaftDrag) return;
    shaftView.px = shaftDrag.px - (e.clientX - shaftDrag.x) / shaftDrag.s;
    shaftView.py = shaftDrag.py - (e.clientY - shaftDrag.y) / shaftDrag.s;
    shaftClampPan();
    drawShaft();
});
shaftCanvasEl.addEventListener('pointerup', function () { shaftDrag = null; });
shaftCanvasEl.addEventListener('pointercancel', function () { shaftDrag = null; });
// sin autoscroll del navegador con el botón central sobre el dibujo
shaftCanvasEl.addEventListener('mousedown', function (e) { if (e.button === 1) e.preventDefault(); });

function drawShaft() {
    // valores saneados solo para el dibujo (la validación real es shaftValidate)
    var lv = [];
    for (var i = 0; i < shaftLevels.length; i++) {
        lv.push({
            d: shaftLevels[i].d > 0 ? shaftLevels[i].d : 20,
            l: shaftLevels[i].l > 0 ? shaftLevels[i].l : 20
        });
    }
    var total = 0, dmax = 0;
    for (var i2 = 0; i2 < lv.length; i2++) { total += lv[i2].l; dmax = Math.max(dmax, lv[i2].d); }

    var VBW = SVBW, VBH = SVBH, mL = 86, mR = 86, mT = 84, mB = 46;
    var roomW = VBW - mL - mR, roomH = VBH - mT - mB;
    var sc = Math.min(roomW / total, roomH / dmax);
    var x0 = mL + (roomW - total * sc) / 2, cy = mT + roomH / 2;

    var boxTop = cy - dmax * sc / 2, boxBot = cy + dmax * sc / 2, dimY = boxTop - 34;
    var out = LINE(x0 - 30, cy, x0 + total * sc + 30, cy, INK, 0.8, '7,3,2,3');   // eje

    // tramos fundidos (espejo de GetMergedSegments) + fronteras internas
    var segs = [], splitMarks = [];
    for (var i3 = 0; i3 < lv.length; i3++) {
        var last = segs.length ? segs[segs.length - 1] : null;
        if (last && Math.abs(last.d - lv[i3].d) < 1e-9) {
            splitMarks.push({ x: last.x1 + last.len, d: last.d });
            last.len += lv[i3].l;
        } else {
            segs.push({ x1: (last ? last.x1 + last.len : 0), len: lv[i3].l, d: lv[i3].d });
        }
    }

    // tramos [x1,x2] (mm) donde una ranura COME la silueta exterior (aceptadas y la
    // editada; la original bajo edición se salta igual que en el dibujo de la muesca)
    var eat = [];
    var eatGrvs = shaftGrvs.slice();
    if (editGrv) eatGrvs.push(editGrv);
    for (var ge = 0; ge < eatGrvs.length; ge++) {
        if (editGrv && ge === editGrvIdx) continue;
        var gvE = eatGrvs[ge];
        var ea = grvX1(gvE), eb = ea + gvE.e1;
        if (isFinite(ea) && isFinite(eb) && gvE.e1 > 0 && grvDiam(gvE) > 0) eat.push([ea, eb]);
    }

    // contorno por tramo fundido (sin aristas internas): verticales completas y
    // silueta superior/inferior interrumpida donde una ranura la come
    for (var s2 = 0; s2 < segs.length; s2++) {
        var sg = segs[s2];
        var sxA = x0 + sg.x1 * sc, sxB = sxA + sg.len * sc;
        var syT = cy - sg.d * sc / 2, syB = cy + sg.d * sc / 2;
        out += LINE(sxA, syT, sxA, syB, INK, 1.5) + LINE(sxB, syT, sxB, syB, INK, 1.5);
        var runs = [[sg.x1, sg.x1 + sg.len]];
        for (var gi = 0; gi < eat.length; gi++) {
            var nr = [];
            for (var ri = 0; ri < runs.length; ri++) {
                var r0 = runs[ri][0], r1 = runs[ri][1];
                var e0 = eat[gi][0], eF = eat[gi][1];
                if (eF <= r0 || e0 >= r1) { nr.push([r0, r1]); continue; }
                if (e0 > r0) nr.push([r0, e0]);
                if (eF < r1) nr.push([eF, r1]);
            }
            runs = nr;
        }
        for (var rr = 0; rr < runs.length; rr++) {
            var ra = x0 + runs[rr][0] * sc, rb = x0 + runs[rr][1] * sc;
            out += LINE(ra, syT, rb, syT, INK, 1.5) + LINE(ra, syB, rb, syB, INK, 1.5);
        }
    }
    // líneas de división: discontinuas en rojo sobre el Ø del tramo; si caen dentro
    // de una ranura, la superficie ya no existe ahí → no se dibujan
    for (var s3 = 0; s3 < splitMarks.length; s3++) {
        var sp = splitMarks[s3];
        var spEaten = false;
        for (var se = 0; se < eat.length; se++) {
            if (sp.x > eat[se][0] - 1e-9 && sp.x < eat[se][1] + 1e-9) { spEaten = true; break; }
        }
        if (spEaten) continue;
        out += LINE(x0 + sp.x * sc, cy - sp.d * sc / 2, x0 + sp.x * sc, cy + sp.d * sc / 2, RED, 1.2, '5,4');
    }

    // chavetas aceptadas (y la editada en rojo): ranura en planta proyectada sobre el
    // alzado — estadio de ancho b (extremos redondeados radio b/2) centrado en el eje
    var allKeys = shaftKeys.slice();
    if (editKey) allKeys.push(editKey);
    for (var kk = 0; kk < allKeys.length; kk++) {
        if (editKey && kk === editIdx) continue;    // en edición se dibuja la copia roja, no la original
        var ky = allKeys[kk];
        var x1 = keyX1(ky), x2 = x1 + keySpan(ky);
        if (!isFinite(x1) || !isFinite(x2) || !(ky.b > 0) || !(x2 > x1)) continue;
        var isEdit = editKey && kk === allKeys.length - 1;
        var col = isEdit ? RED : INK;
        var kr = Math.min(ky.b * sc / 2, (x2 - x1) * sc / 2);
        out += SLOT(x0 + x1 * sc, x0 + x2 * sc, cy, kr, col, isEdit ? 1.8 : 1.2, isEdit ? null : '4,3');
    }

    // ranuras de anillo aceptadas (y la editada en rojo): muesca rectangular
    // hasta D3 en ambas siluetas del nivel
    var allGrvs = shaftGrvs.slice();
    if (editGrv) allGrvs.push(editGrv);
    for (var gg = 0; gg < allGrvs.length; gg++) {
        if (editGrv && gg === editGrvIdx) continue;  // en edición se dibuja la copia roja, no la original
        var gv = allGrvs[gg];
        var gx1 = grvX1(gv), gx2 = gx1 + gv.e1;
        var gd = grvDiam(gv);
        if (!isFinite(gx1) || !isFinite(gx2) || !(gv.e1 > 0) || !(gd > 0)) continue;
        var gEdit2 = editGrv && gg === allGrvs.length - 1;
        var gcol = gEdit2 ? RED : INK;
        var gw = gEdit2 ? 1.8 : 1.2;
        var rS = gd * sc / 2, rB = Math.max(0, Math.min(gv.d3 > 0 ? gv.d3 : 0, gd) * sc / 2);
        var ga = x0 + gx1 * sc, gb = x0 + gx2 * sc;
        out += PATH([[ga, cy - rS], [ga, cy - rB], [gb, cy - rB], [gb, cy - rS]], gcol, gw);
        out += PATH([[ga, cy + rS], [ga, cy + rB], [gb, cy + rB], [gb, cy + rS]], gcol, gw);
    }

    // entalladuras aceptadas (y la editada en rojo): muesca DIN 509 en ambas siluetas —
    // rampa a 15° desde la superficie y fondo plano; la E cierra recta en la cara del
    // hombro, la F sigue t2 al otro lado y sale a 8° por la cara
    var allUcs = shaftUcs.slice();
    if (editUc) allUcs.push(editUc);
    for (var uu = 0; uu < allUcs.length; uu++) {
        if (editUc && uu === editUcIdx) continue;   // en edición se dibuja la copia roja, no la original
        var uv = allUcs[uu];
        if (!(uv.bnd >= 1 && uv.bnd <= shaftLevels.length - 1) || !(uv.f > 0) || !(uv.t1 > 0)) continue;
        if (Math.abs(shaftLevels[uv.bnd - 1].d - shaftLevels[uv.bnd].d) < 1e-9) continue;
        var uEdit = editUc && uu === allUcs.length - 1;
        var ucol = uEdit ? RED : INK, uw = uEdit ? 1.8 : 1.2;
        var z = ucZone(uv);
        var uLeft = ucSmallLeft(uv);
        var udir = uLeft ? 1 : -1;                      // sentido rampa → hombro
        var xsh = x0 + (uLeft ? z[1] : z[0]) * sc;      // cara del hombro
        var xfar = x0 + (uLeft ? z[0] : z[1]) * sc;     // extremo de la rampa
        var xrmp = xfar + udir * uv.t1 * UC_RAMP * sc;  // fin de rampa (fondo)
        var urs = z[2] * sc / 2, urb = urs - uv.t1 * sc;
        if (uv.form === 'F' && uv.t2 > 0) {
            var xd = xsh + udir * uv.t2 * sc;                    // fondo pasado el hombro
            var uex = urb + uv.t2 * UC_RAMP8 * sc;               // salida a 8° sobre la cara
            out += PATH([[xfar, cy - urs], [xrmp, cy - urb], [xd, cy - urb], [xsh, cy - uex]], ucol, uw);
            out += PATH([[xfar, cy + urs], [xrmp, cy + urb], [xd, cy + urb], [xsh, cy + uex]], ucol, uw);
        } else {
            out += PATH([[xfar, cy - urs], [xrmp, cy - urb], [xsh, cy - urb], [xsh, cy - urs]], ucol, uw);
            out += PATH([[xfar, cy + urs], [xrmp, cy + urb], [xsh, cy + urb], [xsh, cy + urs]], ucol, uw);
        }
    }

    // puntos de centrado aceptados (y el editado en rojo): semiperfil taladrado en la cara del
    // extremo, espejado arriba/abajo (boca → punta sobre el eje)
    var allChs = shaftChs.slice();
    if (editCh) allChs.push(editCh);
    for (var ci = 0; ci < allChs.length; ci++) {
        if (editCh && ci === editChIdx) continue;   // en edición se dibuja la copia roja, no el original
        var cv = allChs[ci];
        if (!(cv.d1 > 0) || !(cv.d2 > cv.d1) || !(cv.lp > 0)) continue;
        var cEdit = editCh && ci === allChs.length - 1;
        var ccol = cEdit ? RED : INK, cw = cEdit ? 1.8 : 1.2;
        var pp = chProfilePts(cv), topArr = [], botArr = [];
        for (var pi = 0; pi < pp.length; pi++) {
            topArr.push([x0 + pp[pi].x * sc, cy - pp[pi].r * sc]);
            botArr.push([x0 + pp[pi].x * sc, cy + pp[pi].r * sc]);
        }
        out += PATH(topArr, ccol, cw) + PATH(botArr, ccol, cw);
    }

    if (shaftStep === 1) {
        // nivel con foco: resaltado en rojo + cotas Ø y L
        if (shaftFocus >= 0 && shaftFocus < lv.length) {
            var fx = 0;
            for (var f = 0; f < shaftFocus; f++) fx += lv[f].l;
            var flv = lv[shaftFocus];
            var rx1 = x0 + fx * sc, rx2 = rx1 + flv.l * sc;
            var ry1 = cy - flv.d * sc / 2, ry2 = cy + flv.d * sc / 2;
            out += RECT(rx1, ry1, flv.l * sc, flv.d * sc, RED, 1.8);

            out += LINE(rx1, ry1, rx1, dimY, INK, 1) + LINE(rx2, ry1, rx2, dimY, INK, 1);
            out += hDim(rx1, rx2, dimY, 'L' + (shaftFocus + 1) + ' = ' + fmt(shaftLevels[shaftFocus].l || 0));

            var leftHalf = (fx + flv.l / 2) < total / 2;
            var xQ = leftHalf ? x0 - 40 : x0 + total * sc + 40;
            out += LINE(rx1, ry1, xQ, ry1, INK, 1) + LINE(rx1, ry2, xQ, ry2, INK, 1);
            out += vDim(ry1, ry2, xQ, 'Ø' + (shaftFocus + 1) + ' = ' + fmt(shaftLevels[shaftFocus].d || 0));
        }
    } else if (editKey || editGrv || editUc) {
        // aristas seleccionables: marcador azul + zona de clic ancha invisible.
        // Para la entalladura solo son elegibles los HOMBROS (cambio de Ø).
        var cur = editKey || editGrv || editUc;
        var curSel = editUc ? editUc.bnd : cur.edge;
        var xs = shaftBounds();
        for (var e2 = 0; e2 < xs.length; e2++) {
            if (editUc && !shoulderInfo(e2)) continue;
            var ex = x0 + xs[e2] * sc;
            var sel = e2 === curSel;
            out += LINE(ex, boxTop - 10, ex, boxBot + 10, sel ? BLU : '#9AA0A6', sel ? 2.2 : 1, sel ? null : '3,3');
            out += mk('line', {
                x1: n1(ex), y1: n1(boxTop - 14), x2: n1(ex), y2: n1(boxBot + 14),
                stroke: '#000000', 'stroke-opacity': '0', 'stroke-width': 14,
                'pointer-events': 'stroke',      // clicable aunque el trazo sea invisible
                'class': 'edgehit', 'data-edge': e2
            });
        }
        // cota de posición: chaveta → extremo IZQUIERDO siempre; ranura → la pared que
        // indica el signo (positiva = izquierda, negativa = derecha: arista + cota).
        // La línea auxiliar baja hasta la pieza en edición, centrada en la línea de eje.
        // En modo centro-en-arista no hay cota: se marca el centro del arco izq sobre la
        // arista. La entalladura no lleva cota: su posición es el propio hombro.
        var cx1 = editKey ? keyX1(editKey) : editGrv ? grvX1(editGrv) : NaN;
        if (isFinite(cx1)) {
            var exSel = x0 + xs[cur.edge] * sc;
            if (editKey && editKey.ctr) {
                out += CIRC(exSel, cy, 3.5, RED, 1.6);
                out += LINE(exSel - 8, cy, exSel + 8, cy, RED, 1) + LINE(exSel, cy - 8, exSel, cy + 8, RED, 1);
            } else {
                var target = x0 + (editGrv ? xs[cur.edge] + cur.off : cx1) * sc;
                // línea auxiliar: en la ranura arranca de la propia pared (superficie del
                // nivel), no del centro del eje; la chaveta sigue en el eje (va centrada)
                var auxY0 = cy;
                if (editGrv) {
                    var gdAux = grvDiam(editGrv);
                    auxY0 = gdAux > 0 ? cy - gdAux * sc / 2 : cy;
                }
                out += LINE(target, auxY0, target, dimY, INK, 1) + LINE(exSel, boxTop - 10, exSel, dimY, INK, 1);
                out += hDim(exSel, target, dimY, fmt(cur.off) + ' mm');
            }
            // cota de longitud de la chaveta, con las MISMAS referencias que el modelo SW:
            // modo cota = extremo a extremo; modo centro = centro anclado → extremo opuesto
            if (editKey && editKey.l > 0) {
                var kSpanEnd = cx1 + keySpan(editKey);
                var lA = editKey.ctr === 1 ? xs[cur.edge] : cx1;            // centro izq o extremo izq
                var lB = editKey.ctr === 2 ? xs[cur.edge] : kSpanEnd;       // centro der o extremo der
                var kx1s = x0 + lA * sc, kx2s = x0 + lB * sc;
                var krEd = Math.min(editKey.b > 0 ? editKey.b : 0, keySpan(editKey)) * sc / 2;
                var yLen = boxBot + 24;
                out += LINE(kx1s, cy + krEd, kx1s, yLen, INK, 1) + LINE(kx2s, cy + krEd, kx2s, yLen, INK, 1);
                out += hDim(kx1s, kx2s, yLen, 'L = ' + fmt(editKey.l));
            }
            // cotas de la ranura en edición: espesor E1 abajo y fondo Ø D3 en carril
            // lateral (mismo estilo que las cotas de nivel del paso 1)
            if (editGrv) {
                var gdDim = grvDiam(editGrv);
                if (gdDim > 0 && editGrv.e1 > 0) {
                    var ga1 = x0 + cx1 * sc, ga2 = ga1 + editGrv.e1 * sc;
                    var rBd = Math.max(0, Math.min(editGrv.d3 > 0 ? editGrv.d3 : gdDim, gdDim)) * sc / 2;
                    var yE1 = boxBot + 24;
                    out += LINE(ga1, cy + rBd, ga1, yE1, INK, 1) + LINE(ga2, cy + rBd, ga2, yE1, INK, 1);
                    out += hDim(ga1, ga2, yE1, 'E1 = ' + fmt(editGrv.e1));
                    if (editGrv.d3 > 0 && editGrv.d3 < gdDim) {
                        var grvLeftHalf = (cx1 + editGrv.e1 / 2) < total / 2;
                        var xQg = grvLeftHalf ? x0 - 40 : x0 + total * sc + 40;
                        var gxw = grvLeftHalf ? ga1 : ga2;      // pared más cercana al carril
                        out += LINE(gxw, cy - rBd, xQg, cy - rBd, INK, 1) + LINE(gxw, cy + rBd, xQg, cy + rBd, INK, 1);
                        out += vDim(cy - rBd, cy + rBd, xQg, 'D3 = ' + fmt(editGrv.d3));
                    }
                }
            }
        }
    }

    var vb = shaftViewBox();
    out = mk('svg', {
        viewBox: vb[0].toFixed(2) + ' ' + vb[1].toFixed(2) + ' ' + vb[2].toFixed(2) + ' ' + vb[3].toFixed(2),
        preserveAspectRatio: 'xMidYMid meet'
    }, out);
    out += '<div class="zoombar"><button type="button" class="zbtn" data-z="reset" ' +
        'title="Encuadre completo · zoom: rueda · mover: arrastrar con la rueda pulsada">⟲</button></div>';
    shaftCanvasEl.innerHTML = out;
}

// ---- dibujo: sección con la chaveta -----------------------------------------
// Vista desde el extremo derecho: 0° = arriba; ángulo positivo gira en sentido
// horario en pantalla (equivale al sentido +Z→−Y validado en SolidWorks).
function drawKeySection() {
    if (!editKey) { keySectionEl.innerHTML = ''; return; }
    var W = 300, H = 130, cx = W / 2, cyc = H / 2 + 4;
    var refd = editKey.refd > 0 ? editKey.refd : 20;
    var diams = keySpannedDiams(editKey);
    var dmax = refd;
    for (var i = 0; i < diams.length; i++) dmax = Math.max(dmax, diams[i]);
    var s = 52 / (dmax / 2);
    var R = refd / 2;

    var out = CIRC(cx, cyc, R * s, INK, 1.5);
    for (var j = 0; j < diams.length; j++) {
        if (Math.abs(diams[j] - refd) < 1e-9) continue;
        out += CIRC(cx, cyc, diams[j] / 2 * s, '#9AA0A6', 1, '4,3');   // otros niveles atravesados
    }
    out += LINE(cx - 4, cyc, cx + 4, cyc, INK, 0.8) + LINE(cx, cyc - 4, cx, cyc + 4, INK, 0.8); // centro

    var b2 = editKey.b > 0 ? editKey.b / 2 : 3;
    var t = editKey.depth > 0 ? editKey.depth : 3;
    var rf = Math.max(0.5, R - t);
    var wallOut = b2 < R ? Math.sqrt(R * R - b2 * b2) : R;
    var cnt = Math.max(1, editKey.cnt || 1);
    for (var kI = 0; kI < cnt; kI++) {
        var phi = ((editKey.ang || 0) + kI * 360 / cnt) * Math.PI / 180;
        var sin = Math.sin(phi), cos = Math.cos(phi);
        // punto local (u = tangencial, v = radial) → pantalla
        function P(u, v) { return [cx + (v * sin + u * cos) * s, cyc - (v * cos - u * sin) * s]; }
        out += PATH([P(-b2, wallOut), P(-b2, rf), P(b2, rf), P(b2, wallOut)], kI === 0 ? RED : '#C0392B', 1.6);
    }
    out += TEXT(cx, H - 4, 'Sección · Ø' + fmt(refd) + ' · fondo Ø' + fmt(2 * rf) + (cnt > 1 ? ' · ' + cnt + ' uds' : ''), '#5A6068', false);

    keySectionEl.innerHTML = mk('svg', { viewBox: '0 0 ' + W + ' ' + H, preserveAspectRatio: 'xMidYMid meet' }, out);
}
