// Lógica del configurador de pieza (Bulón). Cargado por configurator.html.
//
// La tabla DIN 471 la inyecta el host en window.DIN471 antes de este script
// (ver PartConfiguratorDialog.AddScriptToExecuteOnDocumentCreatedAsync). Si abres el .html
// suelto en el navegador, window.DIN471 no existe y se usa una tabla vacía.
//
// La vista previa de la izquierda es un <svg> que construye draw(): TODO atributo se entrecomilla
// vía mk(); un valor sin comillas antes de '/>' rompe el autocierre de la etiqueta y esta se traga
// a sus hermanos (ese era el bug del render). Nunca escribas etiquetas SVG a mano: usa mk()/LINE/RECT/PATH/POLY/TEXT.

var DIN471 = window.DIN471 || { rows: [] };

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
var INK = '#1C1C1C', RED = '#E10000';

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
function PATH(pts, col, w) {
    var d = 'M ' + n1(pts[0][0]) + ' ' + n1(pts[0][1]);
    for (var i = 1; i < pts.length; i++) d += ' L ' + n1(pts[i][0]) + ' ' + n1(pts[i][1]);
    return mk('path', { d: d, fill: 'none', stroke: col, 'stroke-width': w, 'stroke-linejoin': 'round' });
}
function POLY(pts, col) {
    var p = '';
    for (var i = 0; i < pts.length; i++) p += (i ? ' ' : '') + n1(pts[i][0]) + ',' + n1(pts[i][1]);
    return mk('polygon', { points: p, fill: col });
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
});
