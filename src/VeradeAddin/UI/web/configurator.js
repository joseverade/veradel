// Lógica del configurador de pieza (Bulón + Eje). Cargado por configurator.html.
// Las DOS piezas usan el MISMO asistente (el motor del eje); la tarjeta del
// catálogo solo fija wizMode: 'S' = eje (8 pasos) o 'B' = bulón (pasos 1, 3 y 8
// con el cuerpo fijo en 2 niveles cabeza + vástago). Ver el bloque ASISTENTE.
//
// La tabla DIN 471 la inyecta el host en window.DIN471 antes de este script
// (ver PartConfiguratorDialog.AddScriptToExecuteOnDocumentCreatedAsync). Si abres el .html
// suelto en el navegador, window.DIN471 no existe y se usa una tabla vacía. Para re-editar
// una pieza registrada, el host inyecta además window.PRELOAD (ver loadPreload al final).
//
// La vista previa de la izquierda es un <svg> que construye drawShaft(): TODO atributo se entrecomilla
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

var CH_TAN30 = Math.tan(30 * Math.PI / 180);   // flanco del avellanado 60° (semiángulo 30°)
var CH_TAN60 = Math.tan(60 * Math.PI / 180);   // protección 120° y punta de broca 120° (semiángulo 60°)

// Tablas DIN 332 (puntos de centrado). Valores autorizados — ver resources/data/din_332.txt y las
// figuras din_332_{R,A,B,C}.png (DIN 332-1) + IS/ISO 2540 (DIN 332-2 roscadas). t = prof. funcional
// mínima (sin rosca). Espejo del modelo host ShaftCenterHole.
// DIN 332-1 · sin rosca, por Ø de broca d1.
var DIN332_R = [   // flanco de radio (R derivado): d1, d2, t
    { d1: 0.5, d2: 1.06, t: 1.4 }, { d1: 0.8, d2: 1.7, t: 1.5 }, { d1: 1.0, d2: 2.12, t: 1.9 },
    { d1: 1.25, d2: 2.65, t: 2.3 }, { d1: 1.6, d2: 3.35, t: 2.9 }, { d1: 2.0, d2: 4.25, t: 3.7 },
    { d1: 2.5, d2: 5.3, t: 4.6 }, { d1: 3.15, d2: 6.7, t: 5.8 }, { d1: 4.0, d2: 8.5, t: 7.4 },
    { d1: 5.0, d2: 10.6, t: 9.2 }, { d1: 6.3, d2: 13.2, t: 11.4 }, { d1: 8.0, d2: 17.0, t: 14.7 },
    { d1: 10.0, d2: 21.2, t: 18.3 }, { d1: 12.5, d2: 26.5, t: 23.6 }
];
var DIN332_A = [   // recto 60°: d1, d2, t (tmin)
    { d1: 0.5, d2: 1.06, t: 1.0 }, { d1: 0.8, d2: 1.7, t: 1.5 }, { d1: 1.0, d2: 2.12, t: 1.9 },
    { d1: 1.25, d2: 2.65, t: 2.3 }, { d1: 1.6, d2: 3.35, t: 2.9 }, { d1: 2.0, d2: 4.25, t: 3.7 },
    { d1: 2.5, d2: 5.3, t: 4.6 }, { d1: 3.15, d2: 6.7, t: 5.9 }, { d1: 4.0, d2: 8.5, t: 7.4 },
    { d1: 5.0, d2: 10.6, t: 9.2 }, { d1: 6.3, d2: 13.2, t: 11.5 }, { d1: 8.0, d2: 17.0, t: 14.8 },
    { d1: 10.0, d2: 21.2, t: 18.4 }, { d1: 12.5, d2: 26.5, t: 23.6 }, { d1: 16.0, d2: 33.5, t: 30.0 },
    { d1: 20.0, d2: 42.5, t: 37.5 }, { d1: 25.0, d2: 53.0, t: 47.5 }, { d1: 31.5, d2: 67.0, t: 60.0 },
    { d1: 40.0, d2: 85.0, t: 75.0 }, { d1: 50.0, d2: 106.0, t: 95.0 }
];
var DIN332_B = [   // recto 60° + protección cónica 120°: d1, d2, b, d3, t (tmin)
    { d1: 1.0, d2: 2.12, b: 0.3, d3: 3.15, t: 2.2 }, { d1: 1.25, d2: 2.65, b: 0.4, d3: 4.0, t: 2.7 },
    { d1: 1.6, d2: 3.35, b: 0.5, d3: 5.0, t: 3.4 }, { d1: 2.0, d2: 4.25, b: 0.8, d3: 6.3, t: 4.3 },
    { d1: 2.5, d2: 5.3, b: 0.8, d3: 8.0, t: 5.4 }, { d1: 3.15, d2: 6.7, b: 0.9, d3: 10.0, t: 6.8 },
    { d1: 4.0, d2: 8.5, b: 1.2, d3: 12.5, t: 8.6 }, { d1: 5.0, d2: 10.6, b: 1.6, d3: 16.0, t: 10.8 },
    { d1: 6.3, d2: 13.2, b: 1.4, d3: 18.0, t: 12.9 }, { d1: 8.0, d2: 17.0, b: 1.6, d3: 22.4, t: 16.4 },
    { d1: 10.0, d2: 21.2, b: 2.0, d3: 28.0, t: 20.4 }, { d1: 12.5, d2: 26.5, b: 2.0, d3: 33.5, t: 25.6 },
    { d1: 16.0, d2: 33.5, b: 2.6, d3: 42.5, t: 32.6 }, { d1: 20.0, d2: 42.5, b: 3.0, d3: 53.0, t: 40.5 },
    { d1: 25.0, d2: 53.0, b: 2.9, d3: 63.0, t: 50.4 }, { d1: 31.5, d2: 67.0, b: 3.8, d3: 80.0, t: 63.8 },
    { d1: 40.0, d2: 85.0, b: 4.3, d3: 100.0, t: 79.3 }, { d1: 50.0, d2: 106.0, b: 5.5, d3: 125.0, t: 100.5 }
];
var DIN332_C = [   // recto 60° + protección truncada 60°: d1, d2, b, d4, d5, t (tmin)
    { d1: 1.0, d2: 2.12, b: 0.4, d4: 4.5, d5: 5.0, t: 1.9 }, { d1: 1.25, d2: 2.65, b: 0.6, d4: 5.3, d5: 6.0, t: 2.3 },
    { d1: 1.6, d2: 3.35, b: 0.7, d4: 6.3, d5: 7.1, t: 2.9 }, { d1: 2.0, d2: 4.25, b: 0.9, d4: 7.5, d5: 8.5, t: 3.7 },
    { d1: 2.5, d2: 5.3, b: 0.9, d4: 9.0, d5: 10.0, t: 4.6 }, { d1: 3.15, d2: 6.7, b: 1.1, d4: 11.2, d5: 12.5, t: 5.9 },
    { d1: 4.0, d2: 8.5, b: 1.7, d4: 14.0, d5: 16.0, t: 7.4 }, { d1: 5.0, d2: 10.6, b: 1.7, d4: 18.0, d5: 20.0, t: 9.2 },
    { d1: 6.3, d2: 13.2, b: 2.3, d4: 22.4, d5: 25.0, t: 11.5 }, { d1: 8.0, d2: 17.0, b: 3.0, d4: 28.0, d5: 31.5, t: 14.8 },
    { d1: 10.0, d2: 21.2, b: 3.9, d4: 35.5, d5: 40.0, t: 18.4 }, { d1: 12.5, d2: 26.5, b: 4.3, d4: 45.0, d5: 50.0, t: 23.6 },
    { d1: 16.0, d2: 33.5, b: 6.1, d4: 56.0, d5: 63.0, t: 30.0 }, { d1: 20.0, d2: 42.5, b: 7.8, d4: 71.0, d5: 80.0, t: 37.5 },
    { d1: 25.0, d2: 53.0, b: 8.7, d4: 90.0, d5: 100.0, t: 47.5 }, { d1: 31.5, d2: 67.0, b: 11.3, d4: 112.0, d5: 125.0, t: 60.0 },
    { d1: 40.0, d2: 85.0, b: 17.3, d4: 140.0, d5: 160.0, t: 75.0 }, { d1: 50.0, d2: 106.0, b: 17.3, d4: 180.0, d5: 200.0, t: 95.0 }
];
// DIN 332-2 · roscadas D/DR/DS, por rosca M (una tabla). d2 = broca núcleo, d3 = asiento, d4 = boca 60°,
// d5 = boca protección 120° (DS), R = radio esférico (DR). min/max = rango de Ø de eje (norma).
var DIN332_T = [
    { m: 3, d2: 2.5, d3: 3.2, d4: 5.3, d5: 5.8, R: 4.0, t1: 9, t2: 12, t3: 2.6, t4: 1.8, t5: 0.2, min: 7, max: 10 },
    { m: 4, d2: 3.3, d3: 4.3, d4: 6.7, d5: 7.4, R: 5.0, t1: 10, t2: 14, t3: 3.2, t4: 2.1, t5: 0.3, min: 10, max: 13 },
    { m: 5, d2: 4.2, d3: 5.3, d4: 8.1, d5: 8.8, R: 6.3, t1: 12.5, t2: 17, t3: 4.0, t4: 2.4, t5: 0.3, min: 13, max: 16 },
    { m: 6, d2: 5.0, d3: 6.4, d4: 9.6, d5: 10.5, R: 8.0, t1: 16, t2: 21, t3: 5.0, t4: 2.8, t5: 0.4, min: 16, max: 21 },
    { m: 8, d2: 6.8, d3: 8.4, d4: 12.2, d5: 13.2, R: 10.0, t1: 19, t2: 25, t3: 6.0, t4: 3.3, t5: 0.4, min: 21, max: 24 },
    { m: 10, d2: 8.5, d3: 10.5, d4: 14.9, d5: 16.3, R: 16.0, t1: 22, t2: 30, t3: 7.5, t4: 3.8, t5: 0.6, min: 24, max: 30 },
    { m: 12, d2: 10.2, d3: 13.0, d4: 18.1, d5: 19.8, R: 20.0, t1: 28, t2: 37, t3: 9.5, t4: 4.4, t5: 0.7, min: 30, max: 38 },
    { m: 16, d2: 14.0, d3: 17.0, d4: 23.0, d5: 25.3, R: 25.0, t1: 36, t2: 45, t3: 12, t4: 5.2, t5: 1.0, min: 38, max: 50 },
    { m: 20, d2: 17.5, d3: 21.0, d4: 28.4, d5: 31.3, R: 31.5, t1: 42, t2: 53, t3: 15, t4: 6.4, t5: 1.3, min: 50, max: 85 },
    { m: 24, d2: 21.0, d3: 25.0, d4: 34.2, d5: 38.0, R: 40.0, t1: 50, t2: 63, t3: 18, t4: 8, t5: 1.6, min: 85, max: 130 }
];
// Recomendación de tamaño sin rosca por Ø de eje (taller; NO norma). Elige d1 por el primer max ≥ Ø.
var CH_REC_D1 = [
    { d1: 1.0, max: 8 }, { d1: 1.6, max: 12 }, { d1: 2.0, max: 18 }, { d1: 2.5, max: 24 },
    { d1: 3.15, max: 30 }, { d1: 4.0, max: 42 }, { d1: 5.0, max: 58 }, { d1: 6.3, max: 80 },
    { d1: 8.0, max: 115 }, { d1: 10.0, max: 150 }, { d1: 12.5, max: Infinity }
];
function chIsThreaded(f) { return f === 'D' || f === 'DR' || f === 'DS'; }

// Pasos métricos ISO por Ø nominal (get-it-made.co.uk/resources/metric-thread-chart,
// consultada 2026-07-12): el PRIMERO de cada lista es el paso grueso (preseleccionado);
// el resto, finos. Ø sin entrada = métrica no estándar → paso solo a mano (paso 6).
var METRIC_PITCHES = {
    1: [0.25], 1.1: [0.25], 1.2: [0.25], 1.4: [0.3, 0.2], 1.6: [0.35, 0.2], 1.8: [0.35, 0.2],
    2: [0.4, 0.25], 2.2: [0.45, 0.25], 2.5: [0.45, 0.35], 3: [0.5, 0.35], 3.5: [0.6, 0.35],
    4: [0.7, 0.5], 4.5: [0.75, 0.5], 5: [0.8, 0.5], 5.5: [0.5], 6: [1, 0.75], 7: [1, 0.75],
    8: [1.25, 1, 0.75], 9: [1.25, 1, 0.75], 10: [1.5, 1.25, 1, 0.75], 11: [1.5, 1, 0.75],
    12: [1.75, 1.5, 1.25, 1], 14: [2, 1.5, 1.25, 1], 15: [1.5, 1], 16: [2, 1.5, 1], 17: [1.5, 1],
    18: [2.5, 2, 1.5, 1], 20: [2.5, 2, 1.5, 1], 22: [2.5, 2, 1.5, 1], 24: [3, 2, 1.5, 1],
    25: [2, 1.5, 1], 27: [3, 2, 1.5, 1], 28: [2, 1.5, 1], 30: [3.5, 3, 2, 1.5, 1],
    32: [2, 1.5], 33: [3.5, 3, 2, 1.5], 35: [2, 1.5], 36: [4, 3, 2, 1.5], 39: [4, 3, 2, 1.5],
    40: [3, 2, 1.5], 42: [4.5, 4, 3, 2, 1.5], 45: [4, 3, 2, 1.5], 48: [5, 4, 3, 2, 1.5],
    50: [3, 2, 1.5], 52: [5, 4, 3, 2, 1.5], 55: [4, 3, 2, 1.5], 56: [5.5, 4, 3, 2, 1.5],
    58: [4, 3, 2, 1.5], 60: [5.5, 4, 3, 2, 1.5], 62: [4, 3, 2, 1.5], 64: [6, 4, 3, 2, 1.5], 68: [6]
};
// pasos estándar del Ø (clave exacta de la tabla) o [] si el Ø no es métrica estándar
function thPitches(d) {
    for (var k in METRIC_PITCHES) { if (Math.abs(parseFloat(k) - d) < 1e-9) return METRIC_PITCHES[k]; }
    return [];
}
// Ø de núcleo (menor) de la rosca métrica exterior: d3 ≈ d − 1.226869·P (ISO 965)
function thMinor(d, p) { return d - 1.226869 * p; }
function chIsRadius(f) { return f === 'R' || f === 'DR'; }

var catalog = document.getElementById('catalog');

function fmt(v) { return Math.round(v * 100) / 100; }

function post(msg) {
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
}

// ---- búsqueda DIN 471 (fila donde d == Ø del nivel) ----
function dinLookup(d) {
    var rows = (DIN471 && DIN471.rows) ? DIN471.rows : [];
    for (var i = 0; i < rows.length; i++) {
        if (Math.abs(rows[i].d - d) < 1e-9) return rows[i];
    }
    return null;
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
// arco circular (barrido corto) de A a B con centro C conocido: el radio y el sentido
// salen de la geometría (producto vectorial en coords de pantalla) — para los redondeos
function ARCC(ax, ay, bx, by, ccx, ccy, col, w) {
    var r = Math.sqrt((ax - ccx) * (ax - ccx) + (ay - ccy) * (ay - ccy));
    var sweep = ((ax - ccx) * (by - ccy) - (ay - ccy) * (bx - ccx)) > 0 ? 1 : 0;
    return mk('path', {
        d: 'M ' + n1(ax) + ' ' + n1(ay) + ' A ' + n1(r) + ' ' + n1(r) + ' 0 0 ' + sweep + ' ' + n1(bx) + ' ' + n1(by),
        fill: 'none', stroke: col, 'stroke-width': w
    });
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

document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { post('cancel'); }
    if (e.key === 'Enter' && !shaftSec.classList.contains('hidden')) {
        if (editKey && !btnKeyOk.disabled) { btnKeyOk.click(); }
        else if (editGrv && !btnGrvOk.disabled) { btnGrvOk.click(); }
        else if (editUc && !btnUcOk.disabled) { btnUcOk.click(); }
        else if (editCh && !btnChOk.disabled) { btnChOk.click(); }
        else if (editTh && !btnThOk.disabled) { btnThOk.click(); }
        else if (!editKey && !editGrv && !editUc && !editCh && !editTh && !shaftNextBtn.disabled) { shaftNextBtn.click(); }
    }
});

// ================================================================
// ASISTENTE (motor del eje). Dos modos, elegidos por la tarjeta del catálogo:
//  - modo S (EJE): los 8 pasos completos descritos abajo.
//  - modo B (BULON): el MISMO motor reducido: paso 1 cuerpo FIJO en 2 niveles
//    (cabeza Ø1 × L1 y vástago Ø2 × L2, con Ø2 < Ø1), paso 2 ranuras de anillo
//    y paso 3 chaflanes a 45° (internamente los pasos 1, 3 y 8 del eje).
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
//  corte de revolución coaxial en la cara. 7 formas: A/B/C/R (DIN 332-1,
//  sin rosca) y D/DR/DS (DIN 332-2, roscadas). Se elige extremo (izq/der),
//  tipo y tamaño (recomendado por Ø de eje, editable con el checkbox). La
//  medida sale de DIN332_{R,A,B,C,T} según el Ø del extremo. Perfil único
//  en chProfile (espejo de ShaftCenterHole.ProfileMm del host).
//  Paso 6 · Roscas cosméticas: una por nivel como máximo, sobre el
//  cilindro del nivel. M = Ø del nivel (auto); paso del combo ISO
//  (METRIC_PITCHES, grueso primero) o a mano si el Ø no es estándar o
//  el usuario elige "Otro". Arranca en el borde izq/der del nivel y
//  cubre todo el nivel (depth 0) o una profundidad ≤ L del nivel.
//  Espejo de ShaftSpec.ValidateThread.
//  Paso 7 · Redondeos: un GRUPO = un radio r + varios VÉRTICES
//  elegidos con CLIC sobre los puntos rojos del dibujo (sin lista de
//  niveles). Cada nivel expone sus 4 vértices; el simétrico bajo el
//  eje es el MISMO anillo 3D, así que al marcar uno el otro queda
//  marcado y no puede volver a elegirse. Id de vértice =
//  2·nivel + lado (0 izq / 1 der). Elegibles: extremos y hombros
//  reales (una división de Ø iguales no tiene esquina). En un hombro
//  los DOS vértices (cóncavo del Ø menor y convexo del Ø mayor) son
//  independientes y pueden operarse a la vez si caben en la altura
//  (s + s' < h). Otro radio = otro grupo (otra operación fillet).
//  Espejo de ShaftSpec.ValidateFillet.
//  Paso 8 · Chaflanes: ángulo FIJO a 45°; un GRUPO = una longitud c
//  (cateto) + varios vértices, misma selección. En SolidWorks la
//  operación va con propagación tangente para que cada anillo salga
//  CONECTADO entero. Espejo de ShaftSpec.ValidateChamfer.
//  Protocolo: create|shaft|wiz|n|d,l...|K|<chavetas×9>|G|<ranuras×4>|
//             U|<entalladuras×6>|C|<puntos×15: extremo,tipo,d1,d2,d3,d4,d5,b,R,t,t1,t2,t3,t4,t5>|
//             T|<roscas×4: nivel(0-based),lado(0 izq/1 der),paso,profundidad(0 = todo el nivel)>|
//             F|<redondeos, VARIABLE: radio,m,vértice1..vérticem>|
//             H|<chaflanes, VARIABLE: longitud,m,vértice1..vérticem>
//  (wiz = modo del asistente: 'S' eje, 'B' bulón. La geometría es la misma;
//  el host guarda el mensaje entero en el registro y lo reinyecta en
//  window.PRELOAD para re-editar la pieza con el mismo modo.)
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
var chDetailEl = document.getElementById('chDetail');
var chSectionEl = document.getElementById('chSection');
var chEditChk = document.getElementById('chEdit');
var thListEl = document.getElementById('thList');
var thFormEl = document.getElementById('thForm');
var btnAddTh = document.getElementById('btnAddTh');
var btnThOk = document.getElementById('btnThOk');
var btnThDel = document.getElementById('btnThDel');
var tLvlSel = document.getElementById('tlvl');
var tSideSel = document.getElementById('tside');
var tmEl = document.getElementById('tm');
var tPitchSel = document.getElementById('tpitch');
var tPitchCustomWrap = document.getElementById('tpitchCustomWrap');
var tPitchCustomEl = document.getElementById('tpitchCustom');
var tModeSel = document.getElementById('tmode');
var tDepthWrap = document.getElementById('tdepthWrap');
var tDepthEl = document.getElementById('tdepth');
var thNoteEl = document.getElementById('thNote');
var filListEl = document.getElementById('filList');
var filFormEl = document.getElementById('filForm');
var btnAddFil = document.getElementById('btnAddFil');
var btnFilOk = document.getElementById('btnFilOk');
var btnFilDel = document.getElementById('btnFilDel');
var fradEl = document.getElementById('frad');
var filNoteEl = document.getElementById('filNote');
var chmListEl = document.getElementById('chmList');
var chmFormEl = document.getElementById('chmForm');
var btnAddChm = document.getElementById('btnAddChm');
var btnChmOk = document.getElementById('btnChmOk');
var btnChmDel = document.getElementById('btnChmDel');
var clenEl = document.getElementById('clen');
var chmNoteEl = document.getElementById('chmNote');

var wizMode = 'S';       // 'S' = eje (8 pasos) · 'B' = bulón (pasos 1, 3 y 8 con 2 niveles fijos)
var shaftLevels = [{ d: 20, l: 30 }, { d: 30, l: 40 }, { d: 20, l: 30 }];
var shaftFocus = 0;      // nivel con foco en el paso 1 (rojo + cotas)
var shaftStep = 1;       // paso INTERNO del motor (numeración del eje, también en modo bulón)
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
var shaftThs = [];       // roscas cosméticas aceptadas
var editTh = null;       // rosca en edición (copia de trabajo) o null
var editThIdx = -1;      // índice en shaftThs si se edita una existente; -1 = nueva
var shaftFils = [];      // grupos de redondeo aceptados: { r, edges: [vértice = 2·nivel + lado...] }
var editFil = null;      // redondeo en edición (copia de trabajo) o null
var editFilIdx = -1;     // índice en shaftFils si se edita uno existente; -1 = nuevo
var shaftChms = [];      // grupos de chaflán 45° aceptados: { c, edges: [vértice...] }
var editChm = null;      // chaflán en edición (copia de trabajo) o null
var editChmIdx = -1;     // índice en shaftChms si se edita uno existente; -1 = nuevo

var KTOL = 1e-6;

// secuencia de pasos INTERNOS visibles según el modo (los ids sgrpN no cambian)
function wizSteps() { return wizMode === 'B' ? [1, 3, 8] : [1, 2, 3, 4, 5, 6, 7, 8]; }

// entra al asistente desde una tarjeta del catálogo. Cambiar de modo resetea el
// estado (los valores de un eje no tienen sentido en un bulón y viceversa);
// reabrir la misma pieza conserva lo tecleado, como siempre.
function startWizard(mode) {
    if (mode !== wizMode) {
        wizMode = mode;
        shaftLevels = mode === 'B'
            ? [{ d: 30, l: 5 }, { d: 20, l: 25 }]
            : [{ d: 20, l: 30 }, { d: 30, l: 40 }, { d: 20, l: 30 }];
        shaftKeys = []; shaftGrvs = []; shaftUcs = []; shaftChs = []; shaftThs = [];
        shaftFils = []; shaftChms = [];
        shaftNEl.value = shaftLevels.length;
    }
    catalog.classList.add('hidden'); shaftSec.classList.remove('hidden');
    shaftStep = 1; shaftFocus = 0;
    closeKeyForm(); closeGrvForm(); closeUcForm(); closeChForm(); closeThForm(); closeFilForm(); closeChmForm();
    buildLevelRows(); shaftRender();
}
document.getElementById('cardShaft').addEventListener('click', function () { startWizard('S'); });
document.getElementById('cardBolt').addEventListener('click', function () { startWizard('B'); });

shaftBackBtn.addEventListener('click', function () {
    if (editKey) { closeKeyForm(); shaftUpdate(); return; }
    if (editGrv) { closeGrvForm(); shaftUpdate(); return; }
    if (editUc) { closeUcForm(); shaftUpdate(); return; }
    if (editCh) { closeChForm(); shaftUpdate(); return; }
    if (editTh) { closeThForm(); shaftUpdate(); return; }
    if (editFil) { closeFilForm(); shaftUpdate(); return; }
    if (editChm) { closeChmForm(); shaftUpdate(); return; }
    var seq = wizSteps(), pos = seq.indexOf(shaftStep);
    if (pos > 0) { shaftStep = seq[pos - 1]; shaftRender(); return; }
    shaftSec.classList.add('hidden'); catalog.classList.remove('hidden');
});
// bloqueo de avance por paso interno: en edición o con un elemento inválido no se sale
function stepBlocked(step) {
    if (step === 1) return shaftValidate() !== null;
    if (step === 2) return editKey !== null || shaftFirstBadKey() !== null;
    if (step === 3) return editGrv !== null || shaftFirstBadGrv() !== null;
    if (step === 4) return editUc !== null || shaftFirstBadUc() !== null;
    if (step === 5) return editCh !== null || shaftFirstBadCh() !== null;
    if (step === 6) return editTh !== null || shaftFirstBadTh() !== null;
    if (step === 7) return editFil !== null || shaftFirstBadFil() !== null;
    return editChm !== null || shaftFirstBadChm() !== null;
}
shaftNextBtn.addEventListener('click', function () {
    if (stepBlocked(shaftStep)) return;
    var seq = wizSteps(), pos = seq.indexOf(shaftStep);
    if (pos < seq.length - 1) { shaftStep = seq[pos + 1]; shaftRender(); return; }
    shaftSubmit();
});
shaftNEl.addEventListener('input', function () {
    if (wizMode === 'B') { shaftNEl.value = 2; return; }   // el bulón SIEMPRE tiene 2 niveles
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
    var msg = ['create', 'shaft', wizMode, shaftLevels.length];
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
        msg.push(cv.end, cv.form, cv.d1, cv.d2, cv.d3, cv.d4, cv.d5, cv.b, cv.rr, cv.t, cv.t1, cv.t2, cv.t3, cv.t4, cv.t5);
    }
    msg.push(shaftThs.length);
    for (var t = 0; t < shaftThs.length; t++) {
        var tv = shaftThs[t];
        msg.push(tv.lvl, tv.side, tv.pitch, tv.depth);   // depth 0 = todo el nivel
    }
    msg.push(shaftFils.length);
    for (var f = 0; f < shaftFils.length; f++) {
        var fv = shaftFils[f];
        msg.push(fv.r, fv.edges.length);
        for (var fe = 0; fe < fv.edges.length; fe++) msg.push(fv.edges[fe]);
    }
    msg.push(shaftChms.length);
    for (var h = 0; h < shaftChms.length; h++) {
        var hv = shaftChms[h];
        msg.push(hv.c, hv.edges.length);
        for (var he = 0; he < hv.edges.length; he++) msg.push(hv.edges[he]);
    }
    post(msg.join('|'));
}

// ---- paso 1: niveles ----------------------------------------------------

// (re)construye las filas de niveles conservando los valores actuales
function buildLevelRows() {
    var html = '';
    for (var i = 0; i < shaftLevels.length; i++) {
        // longitud primero y diámetro después (sólo orden visual; el protocolo sigue d,l)
        // en modo bulón los 2 niveles se llaman por su nombre, no por número
        var idxLabel = wizMode === 'B'
            ? '<span class="idx lbl">' + (i === 0 ? 'Cabeza' : 'Vástago') + '</span>'
            : '<span class="idx">' + (i + 1) + '</span>';
        html += '<div class="lvlrow" id="lvlrow' + i + '">' +
            idxLabel +
            '<div class="inp"><input id="lvll' + i + '" type="number" min="0" step="any" value="' + shaftLevels[i].l + '"><span class="u">L</span></div>' +
            '<div class="inp"><input id="lvld' + i + '" type="number" min="0" step="any" value="' + shaftLevels[i].d + '"><span class="u">Ø</span></div>' +
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
                if (editCh) syncChSize();   // recalcula la recomendación por el nuevo Ø del extremo
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
            return (wizMode === 'B' ? (i === 0 ? 'Cabeza' : 'Vástago') : 'Nivel ' + (i + 1)) +
                ': Ø y L deben ser mayores que 0.';
        }
    }
    if (wizMode === 'B') {
        if (shaftLevels.length !== 2) return 'El bulón tiene exactamente 2 niveles (cabeza y vástago).';
        if (!(shaftLevels[1].d < shaftLevels[0].d)) {
            return 'El Ø del vástago (Ø2) debe ser menor que el de la cabeza (Ø1 = ' + fmt(shaftLevels[0].d) + ').';
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
    if (editTh) {
        // clic en el área de un nivel = elegirlo para la rosca (paso 6)
        var lvlIdx = t.getAttribute('data-lvl');
        if (lvlIdx === null) return;
        editTh.lvl = parseInt(lvlIdx, 10) || 0;
        tLvlSel.value = String(editTh.lvl);
        editTh.side = thDefaultSide(editTh.lvl);    // arranque en el borde exterior del nivel
        tSideSel.value = String(editTh.side);
        syncThPitch(true);              // nuevo Ø → recomienda su paso grueso
        shaftUpdate();
        return;
    }
    if (editFil || editChm) {
        // multiselección: clic en un vértice rojo = añadirlo/quitarlo del grupo; el vértice
        // simétrico es el mismo anillo (mismo id) y un vértice ocupado por otro grupo se ignora
        var cidT = t.getAttribute('data-corner');
        if (cidT === null) return;
        var cid = parseInt(cidT, 10);
        if (!cornerExists(cid) || cornerTaken(cid)) return;
        toggleCornerEdge(editFil || editChm, cid);
        shaftUpdate();
        return;
    }
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
function chTable(form) {
    return form === 'A' ? DIN332_A : form === 'B' ? DIN332_B : form === 'C' ? DIN332_C
        : form === 'R' ? DIN332_R : DIN332_T;   // D / DR / DS
}
function chRowKey(form, row) { return chIsThreaded(form) ? row.m : row.d1; }
// Ø de boca de una fila (para la etiqueta del combo): C→d5, B→d3, roscada→d4, A/R→d2.
function chSizeMouth(form, row) {
    return form === 'C' ? row.d5 : form === 'B' ? row.d3 : chIsThreaded(form) ? row.d4 : row.d2;
}
function chRecommend(form, d) {
    var t = chTable(form);
    if (chIsThreaded(form)) {
        for (var i = 0; i < t.length; i++) if (d > t[i].min && d <= t[i].max + 1e-9) return t[i];
        return t[t.length - 1];
    }
    var recD1 = CH_REC_D1[CH_REC_D1.length - 1].d1;
    for (var j = 0; j < CH_REC_D1.length; j++) if (d <= CH_REC_D1[j].max) { recD1 = CH_REC_D1[j].d1; break; }
    var best = t[0];
    for (var k = 0; k < t.length; k++) {
        if (Math.abs(t[k].d1 - recD1) < 1e-9) return t[k];
        if (t[k].d1 <= recD1 + 1e-9) best = t[k];
    }
    return best;
}

// Semiperfil {profundidad-desde-la-cara, radio} boca → punta + info del arco. Espejo EXACTO de
// ShaftCenterHole.ProfileMm del host: de aquí salen profundidad, Ø de boca, validación y dibujo.
function chProfile(c) {
    var v = [], arcSeg = -1, arcR = 0;
    if (chIsThreaded(c.form)) {
        // IS 2540:2008 / DIN 332-2: TODAS las profundidades desde la CARA (t5 no desplaza a
        // t4/t3/t2 en DS) y t2 = fondo del cilindro de broca; la punta 120° queda MÁS ALLÁ de t2.
        var rc = c.d2 / 2, r3 = c.d3 / 2, r4 = c.d4 / 2, r5 = c.d5 / 2, tip = rc / CH_TAN60;
        if (c.form === 'DS') v.push([0, r5]);
        v.push([c.form === 'DS' ? c.t5 : 0, r4]);
        v.push([c.t4, r3]);
        if (c.form === 'DR') { arcSeg = v.length - 1; arcR = c.rr; }
        v.push([c.t3, r3]);
        v.push([c.t3, rc]);
        v.push([c.t2, rc]);
        v.push([c.t2 + tip, 0]);
    } else {
        var r1 = c.d1 / 2, r2 = c.d2 / 2, hc = (r2 - r1) / CH_TAN30, tipN = r1 / CH_TAN60, prot = 0;
        if (c.form === 'B') { var b3 = c.d3 / 2; prot = (b3 - r2) / CH_TAN60; v.push([0, b3]); v.push([prot, r2]); }
        else if (c.form === 'C') { var q4 = c.d4 / 2, q5 = c.d5 / 2; prot = (q5 - q4) / CH_TAN30; v.push([0, q5]); v.push([prot, q4]); v.push([prot, r2]); }
        else { v.push([0, r2]); }
        v.push([prot + hc, r1]);
        if (c.form === 'R') { var dr = r2 - r1; if (dr > 0) { arcSeg = v.length - 1; arcR = (hc * hc + dr * dr) / (2 * dr); } }
        v.push([c.t, r1]);
        v.push([c.t + tipN, 0]);
    }
    return { v: v, arcSeg: arcSeg, arcR: arcR };
}
// profundidad total y Ø de boca, derivados del perfil (espejo del host)
function chDepth(c) { var v = chProfile(c).v; return v.length ? v[v.length - 1][0] : 0; }
function chMouth(c) { var v = chProfile(c).v; return v.length ? 2 * v[0][1] : 0; }
// radio derivado del flanco (forma R) para mostrarlo en la nota
function chArcR(c) { return chProfile(c).arcR; }

// rellena las medidas de c con la fila elegida según la forma
function applyChRow(row) {
    if (!editCh) return;
    var f = editCh.form;
    editCh._key = chRowKey(f, row);
    if (chIsThreaded(f)) {
        editCh.d1 = row.m; editCh.d2 = row.d2; editCh.d3 = row.d3; editCh.d4 = row.d4;
        editCh.d5 = f === 'DS' ? row.d5 : 0; editCh.rr = f === 'DR' ? row.R : 0; editCh.b = 0; editCh.t = 0;
        editCh.t1 = row.t1; editCh.t2 = row.t2; editCh.t3 = row.t3; editCh.t4 = row.t4;
        editCh.t5 = f === 'DS' ? row.t5 : 0;
    } else {
        editCh.d1 = row.d1; editCh.d2 = row.d2;
        editCh.d3 = f === 'B' ? row.d3 : 0; editCh.d4 = f === 'C' ? row.d4 : 0; editCh.d5 = f === 'C' ? row.d5 : 0;
        editCh.b = (f === 'B' || f === 'C') ? row.b : 0; editCh.rr = 0; editCh.t = row.t;
        editCh.t1 = editCh.t2 = editCh.t3 = editCh.t4 = editCh.t5 = 0;
    }
    renderChDetail(); updateChNote();
}
// (re)construye el combo de tamaños y recomienda por Ø. Rellena las cotas salvo que el punto esté en
// modo "editar" (custom): entonces respeta lo que el usuario metió, salvo forceApply (cambio de forma).
function syncChSize(forceApply) {
    if (!editCh) return;
    var form = editCh.form, t = chTable(form), d = chEndDiam(editCh.end), opts = '';
    for (var i = 0; i < t.length; i++) {
        var key = chRowKey(form, t[i]);
        var lbl = chIsThreaded(form)
            ? ('M' + key + ' · núcleo Ø' + fmt(t[i].d2) + ' · boca Ø' + fmt(t[i].d4))
            : ('d1 Ø' + fmt(t[i].d1) + ' · boca Ø' + fmt(chSizeMouth(form, t[i])));
        opts += '<option value="' + key + '">' + lbl + '</option>';
    }
    csizeSel.innerHTML = opts;
    var found = null;
    for (var j = 0; j < t.length; j++) if (Math.abs(chRowKey(form, t[j]) - editCh._key) < 1e-9) found = t[j];
    if (!found) found = chRecommend(form, d);
    csizeSel.value = String(chRowKey(form, found));
    if (forceApply || !editCh.custom) applyChRow(found);
    else { renderChDetail(); updateChNote(); }
}
// campos (cotas) editables por forma: [clave, etiqueta]
function chFields(form) {
    if (form === 'A' || form === 'R') return [['d1', 'Ø broca d1'], ['d2', 'Ø boca d2'], ['t', 'prof. t']];
    if (form === 'B') return [['d1', 'Ø broca d1'], ['d2', 'Ø boca d2'], ['d3', 'Ø protección d3'], ['b', 'ancho protección b'], ['t', 'prof. t']];
    if (form === 'C') return [['d1', 'Ø broca d1'], ['d2', 'Ø boca d2'], ['d4', 'Ø interior d4'], ['d5', 'Ø exterior d5'], ['b', 'ancho protección b'], ['t', 'prof. t']];
    var f = [['d1', 'Ø rosca M'], ['d2', 'Ø núcleo d2'], ['d3', 'Ø asiento d3'], ['d4', 'Ø boca d4'],
        ['t1', 'rosca útil t1'], ['t2', 'prof. total t2'], ['t3', 'asiento t3'], ['t4', 'contacto t4']];
    if (form === 'DR') f.push(['rr', 'radio contacto R']);
    if (form === 'DS') { f.push(['d5', 'Ø protección d5']); f.push(['t5', 'prof. protección t5']); }
    return f;
}
// detalle con TODAS las cotas de la forma; deshabilitadas salvo que el checkbox "editar" esté marcado
function renderChDetail() {
    if (!chDetailEl) return;
    if (!editCh) { chDetailEl.innerHTML = ''; return; }
    var fields = chFields(editCh.form), html = '';
    for (var i = 0; i < fields.length; i++) {
        html += '<div class="cota"><label>' + fields[i][1] + '</label>' +
            '<input type="number" step="any" min="0" data-k="' + fields[i][0] + '" value="' + fmt(editCh[fields[i][0]] || 0) + '"' +
            (editCh.custom ? '' : ' disabled') + '></div>';
    }
    chDetailEl.innerHTML = html;
    var inps = chDetailEl.querySelectorAll('input');
    for (var j = 0; j < inps.length; j++) {
        inps[j].addEventListener('input', function () {
            if (!editCh) return;
            editCh[this.getAttribute('data-k')] = parseFloat(this.value) || 0;
            updateChNote(); shaftUpdate();
        });
    }
}
function updateChNote() {
    if (!editCh) return;
    var c = editCh;
    var body = chIsThreaded(c.form)
        ? ('M' + fmt(c.d1) + ' · núcleo Ø' + fmt(c.d2) + ' · asiento Ø' + fmt(c.d3) + ' · boca Ø' + fmt(c.d4) +
            (c.form === 'DR' ? ' · R ' + fmt(c.rr) : '') + (c.form === 'DS' ? ' · protección Ø' + fmt(c.d5) : ''))
        : ('d1 Ø' + fmt(c.d1) + ' · boca Ø' + fmt(chMouth(c)) + (c.form === 'R' ? ' · radio ' + fmt(chArcR(c)) : ''));
    chNoteEl.textContent = 'DIN 332-' + (chIsThreaded(c.form) ? '2' : '1') + ' ' + c.form + ' · ' + body +
        ' · prof. ' + fmt(chDepth(c)) + ' mm';
    chNoteEl.className = 'note';
}

// null = válido; espejo de ShaftSpec.ValidateCenterHole del host
function validateCh(c, idx) {
    if (!(c.end === 0 || c.end === 1)) return 'Extremo no válido.';
    if (['A', 'B', 'C', 'R', 'D', 'DR', 'DS'].indexOf(c.form) < 0) return 'Elige el tipo (A, B, C, R, D, DR o DS).';
    if (chIsThreaded(c.form)) {
        if (!(c.d2 > 0) || !(c.d3 > c.d2) || !(c.d4 > c.d3)) return 'Tamaño DIN 332-2 no válido (d2 < d3 < d4).';
        if (!(c.t4 > 0) || !(c.t3 > c.t4) || !(c.t2 > c.t3)) return 'Profundidades roscadas no válidas (0 < t4 < t3 < t2).';
        if (c.form === 'DR' && !(c.rr > 0)) return 'La forma DR necesita radio de contacto R > 0.';
        if (c.form === 'DS' && (!(c.d5 > c.d4) || !(c.t5 > 0))) return 'La forma DS necesita d5 > d4 y t5 > 0.';
    } else {
        if (!(c.d1 > 0) || !(c.d2 > c.d1)) return 'Tamaño DIN 332-1 no válido (d2 debe ser mayor que d1).';
        if (!(c.t > 0)) return 'La profundidad t debe ser > 0.';
        if (c.form === 'B' && !(c.d3 > c.d2)) return 'La forma B necesita d3 > d2.';
        if (c.form === 'C' && (!(c.d4 > c.d2) || !(c.d5 > c.d4))) return 'La forma C necesita d2 < d4 < d5.';
        if ((c.form === 'B' || c.form === 'C') && !(c.b > 0)) return 'El ancho de protección b debe ser > 0.';
    }
    var pr = chProfile(c), v = pr.v;
    if (v.length < 3) return 'Perfil del punto de centrado incompleto.';
    for (var i = 1; i < v.length; i++) {
        if (v[i][0] < v[i - 1][0] - KTOL || v[i][1] > v[i - 1][1] + KTOL) return 'Las cotas dan un perfil imposible.';
    }
    if (!(chDepth(c) > KTOL)) return 'El perfil no tiene profundidad.';
    if (chIsRadius(c.form) && !(pr.arcR > 0)) return 'El radio de contacto es demasiado pequeño.';
    var hasCyl = false;
    for (var h = 1; h < v.length; h++) if (Math.abs(v[h][1] - v[h - 1][1]) < 1e-9 && v[h][0] - v[h - 1][0] > KTOL) hasCyl = true;
    if (!hasCyl) return 'No queda tramo recto de broca.';
    var lv = c.end === 0 ? shaftLevels[0] : shaftLevels[shaftLevels.length - 1];
    if (!(lv.d > 0) || !(lv.l > 0)) return 'El nivel del extremo no es válido.';
    if (!(chMouth(c) < lv.d)) return 'No cabe en la cara del extremo (Ø boca ' + fmt(chMouth(c)) + ' ≥ Ø' + fmt(lv.d) + ').';
    if (!(chDepth(c) < lv.l - KTOL)) return 'Es más profundo que el nivel del extremo (' + fmt(chDepth(c)) + ' ≥ ' + fmt(lv.l) + ' mm).';
    for (var q = 0; q < shaftChs.length; q++) {
        if (q !== idx && shaftChs[q].end === c.end) return 'Ya hay otro punto de centrado en ese extremo.';
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
    editCh = { end: end, form: 'A', custom: false, _key: 0, d1: 0, d2: 0, d3: 0, d4: 0, d5: 0, b: 0, rr: 0, t: 0, t1: 0, t2: 0, t3: 0, t4: 0, t5: 0 };
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
    syncChSize(true); shaftUpdate();   // nueva forma → rellena sus cotas aunque esté en modo editar
});
csizeSel.addEventListener('change', function () {
    if (!editCh) return;
    var t = chTable(editCh.form), key = parseFloat(csizeSel.value);
    for (var i = 0; i < t.length; i++) {
        if (Math.abs(chRowKey(editCh.form, t[i]) - key) < 1e-9) { applyChRow(t[i]); break; }
    }
    shaftUpdate();
});
if (chEditChk) chEditChk.addEventListener('change', function () {
    if (!editCh) return;
    editCh.custom = chEditChk.checked;   // desbloquea/rebloquea las cotas del detalle
    renderChDetail();
});

function openChForm() {
    cEndSel.value = String(editCh.end);
    cTypeSel.value = editCh.form;
    if (chEditChk) chEditChk.checked = !!editCh.custom;
    syncChSize();               // recomienda por Ø y rellena el detalle
    renderChDetail();
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
        var size = chIsThreaded(c.form) ? 'M' + fmt(c.d1) : 'd1 ' + fmt(c.d1);
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
// puntos del semiperfil superior (boca → punta), x absoluto en mm — para el dibujo del alzado.
// Tesela el arco (R/DR) del perfil que devuelve chProfile (fuente única, espejo del host).
function chProfilePts(c) {
    var total = shaftTotal(), xf = c.end === 0 ? 0 : total, sign = c.end === 0 ? 1 : -1;
    var pr = chProfile(c), v = pr.v, out = [];
    for (var i = 0; i < v.length; i++) {
        if (i === pr.arcSeg && pr.arcR > 0) {
            var xa = v[i - 1][0], ya = v[i - 1][1], xb = v[i][0], yb = v[i][1];
            var cx = xb, cy = yb + pr.arcR;
            var a0 = Math.atan2(ya - cy, xa - cx), a1 = Math.atan2(yb - cy, xb - cx), N = 8;
            for (var k = 1; k <= N; k++) { var a = a0 + (a1 - a0) * k / N; out.push({ x: xf + sign * (cx + pr.arcR * Math.cos(a)), r: cy + pr.arcR * Math.sin(a) }); }
        } else {
            out.push({ x: xf + sign * v[i][0], r: v[i][1] });
        }
    }
    return out;
}

// ---- paso 6: roscas cosméticas --------------------------------------------

// tramo axial [x1, x2] que cubre la rosca (mm desde la cara izquierda):
// arranca en el borde elegido del nivel y corre hacia dentro (depth 0 = todo el nivel)
function thSpan(t) {
    var xs = shaftBounds(), lv2 = shaftLevels[t.lvl];
    var len = t.depth > 0 ? t.depth : lv2.l;
    return t.side === 1 ? [xs[t.lvl + 1] - len, xs[t.lvl + 1]] : [xs[t.lvl], xs[t.lvl] + len];
}
// borde de arranque por defecto: el más EXTERIOR del nivel, de modo que la rosca corre
// hacia el CENTRO del eje (mitad izquierda → borde izq., mitad derecha → borde der.)
function thDefaultSide(lvl) {
    var xs = shaftBounds();
    if (!(lvl >= 0) || lvl >= shaftLevels.length) return 0;
    return (xs[lvl] + xs[lvl + 1]) / 2 > xs[xs.length - 1] / 2 ? 1 : 0;
}
// null = válida; espejo de ShaftSpec.ValidateThread del host
function validateTh(t, idx) {
    if (!(t.lvl >= 0 && t.lvl < shaftLevels.length)) return 'Nivel no válido (¿cambiaste los niveles?).';
    var lv2 = shaftLevels[t.lvl];
    if (!(lv2.d > 0) || !(lv2.l > 0)) return 'El nivel elegido no es válido.';
    if (!(t.side === 0 || t.side === 1)) return 'Elige el borde de arranque.';
    if (!(t.pitch > 0)) return 'El paso debe ser mayor que 0.';
    if (!(thMinor(lv2.d, t.pitch) > 0)) return 'El paso es demasiado grande para M' + fmt(lv2.d) + '.';
    if (t.depth < 0) return 'La profundidad no puede ser negativa.';
    if (t.depth > 0 && t.depth > lv2.l + KTOL) {
        return 'La profundidad (' + fmt(t.depth) + ') no puede ser mayor que la longitud del nivel (' + fmt(lv2.l) + ' mm).';
    }
    for (var i = 0; i < shaftThs.length; i++) {
        if (i !== idx && shaftThs[i].lvl === t.lvl) return 'Ya hay otra rosca en ese nivel.';
    }
    // Un redondeo/chaflán en el anillo de arranque no bloquea (el host re-ancla s hacia
    // dentro), salvo que se coma la rosca entera.
    var eaten = cornerOpSizeAt(t.side === 1 ? t.lvl + 1 : t.lvl, lv2.d);
    var usable = t.depth > 0 ? t.depth : lv2.l;
    if (eaten > 0 && !(eaten < usable - KTOL)) {
        return 'El chaflán/redondeo del vértice de arranque cubre toda la rosca.';
    }
    return null;
}
function shaftFirstBadTh() {
    for (var i = 0; i < shaftThs.length; i++) {
        var m = validateTh(shaftThs[i], i);
        if (m !== null) return 'Rosca ' + (i + 1) + ': ' + m;
    }
    return null;
}
// combo de niveles (se reconstruye al abrir: los niveles pueden haber cambiado)
function buildThLvlOptions() {
    var opts = '';
    for (var i = 0; i < shaftLevels.length; i++) {
        opts += '<option value="' + i + '">Nivel ' + (i + 1) + ' · Ø' + fmt(shaftLevels[i].d) +
            ' × ' + fmt(shaftLevels[i].l) + '</option>';
    }
    tLvlSel.innerHTML = opts;
}
// métrica (M = Ø del nivel, solo lectura) + combo de pasos ISO del Ø, con "Otro…" para
// meterlo a mano. reset = true recoloca el paso recomendado (grueso) aunque hubiera uno.
function syncThPitch(reset) {
    if (!editTh) return;
    var d = shaftLevels[editTh.lvl] ? shaftLevels[editTh.lvl].d : 0;
    tmEl.value = 'M' + fmt(d);
    var ps = thPitches(d), opts = '';
    for (var i = 0; i < ps.length; i++) opts += '<option value="' + ps[i] + '">' + fmt(ps[i]) + '</option>';
    opts += '<option value="x">Otro…</option>';
    tPitchSel.innerHTML = opts;
    var inList = false;
    for (var j = 0; j < ps.length; j++) if (Math.abs(ps[j] - editTh.pitch) < 1e-9) inList = true;
    if (reset || (!inList && !editTh.customPitch)) {
        editTh.pitch = ps.length ? ps[0] : 0;   // grueso primero; sin tabla → a mano
        editTh.customPitch = ps.length === 0;
        inList = ps.length > 0;
    }
    tPitchSel.value = editTh.customPitch || !inList ? 'x' : String(editTh.pitch);
    tPitchCustomWrap.classList.toggle('hidden', tPitchSel.value !== 'x');
    if (tPitchSel.value === 'x') tPitchCustomEl.value = editTh.pitch > 0 ? fmt(editTh.pitch) : '';
    updateThNote();
}
function updateThNote() {
    if (!editTh) return;
    var lv2 = shaftLevels[editTh.lvl];
    if (!lv2) { thNoteEl.textContent = ''; return; }
    var std = thPitches(lv2.d).length > 0;
    thNoteEl.textContent = (std ? '' : 'Ø' + fmt(lv2.d) + ' no es métrica estándar: paso a mano. ') +
        'M' + fmt(lv2.d) + (editTh.pitch > 0 ? '×' + fmt(editTh.pitch) + ' · núcleo Ø' + fmt(thMinor(lv2.d, editTh.pitch)) : '') +
        ' · ' + (editTh.depth > 0 ? 'prof. ' + fmt(editTh.depth) + ' mm' : 'todo el nivel (' + fmt(lv2.l) + ' mm)');
    thNoteEl.className = 'note';
}

btnAddTh.addEventListener('click', function () {
    var used = {};
    for (var i = 0; i < shaftThs.length; i++) used[shaftThs[i].lvl] = true;
    var lvl = 0;
    for (var j = 0; j < shaftLevels.length; j++) { if (!used[j]) { lvl = j; break; } }
    editTh = { lvl: lvl, side: thDefaultSide(lvl), pitch: 0, depth: 0, mode: 0, customPitch: false };
    editThIdx = -1;
    openThForm(true); shaftUpdate();
});
btnThOk.addEventListener('click', function () {
    if (!editTh || validateTh(editTh, editThIdx) !== null) return;
    if (editThIdx >= 0) shaftThs[editThIdx] = editTh; else shaftThs.push(editTh);
    closeThForm(); shaftUpdate();
});
btnThDel.addEventListener('click', function () {
    if (editThIdx >= 0) shaftThs.splice(editThIdx, 1);
    closeThForm(); shaftUpdate();
});
tLvlSel.addEventListener('change', function () {
    if (!editTh) return;
    editTh.lvl = parseInt(tLvlSel.value, 10) || 0;
    editTh.side = thDefaultSide(editTh.lvl);    // nuevo nivel → arranque en su borde exterior
    tSideSel.value = String(editTh.side);
    syncThPitch(true); shaftUpdate();   // nuevo Ø → recomienda su paso grueso
});
tSideSel.addEventListener('change', function () {
    if (!editTh) return;
    editTh.side = parseInt(tSideSel.value, 10) || 0;
    shaftUpdate();
});
tPitchSel.addEventListener('change', function () {
    if (!editTh) return;
    if (tPitchSel.value === 'x') {
        editTh.customPitch = true;
        editTh.pitch = parseFloat(tPitchCustomEl.value) || 0;
    } else {
        editTh.customPitch = false;
        editTh.pitch = parseFloat(tPitchSel.value) || 0;
    }
    tPitchCustomWrap.classList.toggle('hidden', tPitchSel.value !== 'x');
    updateThNote(); shaftUpdate();
});
tPitchCustomEl.addEventListener('input', function () {
    if (!editTh) return;
    editTh.pitch = parseFloat(tPitchCustomEl.value) || 0;
    updateThNote(); shaftUpdate();
});
tModeSel.addEventListener('change', function () {
    if (!editTh) return;
    editTh.mode = parseInt(tModeSel.value, 10) || 0;
    tDepthWrap.classList.toggle('hidden', editTh.mode !== 1);
    editTh.depth = editTh.mode === 1 ? (parseFloat(tDepthEl.value) || 0) : 0;
    updateThNote(); shaftUpdate();
});
tDepthEl.addEventListener('input', function () {
    if (!editTh) return;
    if (editTh.mode === 1) editTh.depth = parseFloat(tDepthEl.value) || 0;
    updateThNote(); shaftUpdate();
});

function openThForm(recommend) {
    buildThLvlOptions();
    tLvlSel.value = String(editTh.lvl);
    tSideSel.value = String(editTh.side);
    editTh.mode = editTh.depth > 0 ? 1 : 0;
    tModeSel.value = String(editTh.mode);
    tDepthWrap.classList.toggle('hidden', editTh.mode !== 1);
    tDepthEl.value = editTh.depth > 0 ? fmt(editTh.depth) : '';
    syncThPitch(!!recommend);
    thFormEl.classList.remove('hidden');
    btnAddTh.classList.add('hidden');
    btnThDel.style.display = editThIdx >= 0 ? '' : 'none';
    renderThList();
}
function closeThForm() {
    editTh = null; editThIdx = -1;
    thFormEl.classList.add('hidden');
    btnAddTh.classList.remove('hidden');
    renderThList();
}
function renderThList() {
    var html = '';
    for (var i = 0; i < shaftThs.length; i++) {
        var t = shaftThs[i], lv2 = shaftLevels[t.lvl];
        html += '<div class="keyitem' + (i === editThIdx ? ' editing' : '') + '" data-th="' + i + '">' +
            '<span>' + (i + 1) + ' · M' + (lv2 ? fmt(lv2.d) : '?') + '×' + fmt(t.pitch) +
            ' · nivel ' + (t.lvl + 1) + ' · ' + (t.side === 1 ? 'borde der.' : 'borde izq.') +
            ' · ' + (t.depth > 0 ? 'prof. ' + fmt(t.depth) : 'todo el nivel') + '</span>' +
            '<span class="kx" data-tdel="' + i + '">✕</span></div>';
    }
    thListEl.innerHTML = html;
    var items = thListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-tdel');
            if (del !== null && del !== undefined) {
                shaftThs.splice(parseInt(del, 10), 1);
                closeThForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editThIdx = parseInt(this.getAttribute('data-th'), 10);
            editTh = JSON.parse(JSON.stringify(shaftThs[editThIdx]));
            openThForm(false); shaftUpdate();
        });
    }
}

// ---- pasos 7 y 8: redondeos y chaflanes 45° en VÉRTICES de esquina ---------
//
// Un GRUPO = una medida (radio r o longitud c a 45°) + varios vértices elegidos con clic
// sobre los puntos rojos del dibujo. Id de vértice = 2·nivel + lado (0 izq / 1 der); el
// vértice simétrico bajo el eje es el MISMO anillo 3D (mismo id). Elegibles: extremos y
// hombros reales (una división de Ø iguales no tiene esquina). Los dos vértices de un
// hombro son independientes (cóncavo Ø menor / convexo Ø mayor) y caben juntos si
// s + s' < h. Un vértice admite UNA operación entre todos los grupos. Espejo de
// ShaftSpec.ValidateFillet / ValidateChamfer del host.

function cornerLvl(cid) { return cid >> 1; }
function cornerSide(cid) { return cid & 1; }
// frontera (0 … n) sobre la que cae el vértice
function cornerBnd(cid) { return (cid >> 1) + (cid & 1); }
// el vértice existe: su frontera es extremo del eje o un hombro real
function cornerExists(cid) {
    var lvl = cornerLvl(cid), side = cornerSide(cid), n = shaftLevels.length;
    if (!(cid >= 0) || lvl < 0 || lvl >= n) return false;
    if (side === 0) return lvl === 0 || Math.abs(shaftLevels[lvl - 1].d - shaftLevels[lvl].d) >= 1e-9;
    return lvl === n - 1 || Math.abs(shaftLevels[lvl + 1].d - shaftLevels[lvl].d) >= 1e-9;
}
// el vértice está en una cara de EXTREMO (no en un hombro)
function cornerIsEnd(cid) {
    var lvl = cornerLvl(cid), side = cornerSide(cid);
    return (side === 0 && lvl === 0) || (side === 1 && lvl === shaftLevels.length - 1);
}
// Ø del ANILLO que consume la operación = Ø del propio nivel del vértice
function cornerRingD(cid) { return shaftLevels[cornerLvl(cid)].d; }
// zona axial [z1, z2] que come la operación: s hacia dentro del nivel desde su borde
// (espejo de CornerZoneMm del host)
function cornerZone(cid, s) {
    var xs = shaftBounds(), lvl = cornerLvl(cid);
    return cornerSide(cid) === 0 ? [xs[lvl], xs[lvl] + s] : [xs[lvl + 1] - s, xs[lvl + 1]];
}
// altura del hombro del vértice (0 en un extremo)
function cornerH(cid) {
    if (cornerIsEnd(cid)) return 0;
    var lvl = cornerLvl(cid);
    var nb = cornerSide(cid) === 0 ? lvl - 1 : lvl + 1;
    return Math.abs(shaftLevels[nb].d - shaftLevels[lvl].d) / 2;
}
// tramo continuo [ini, fin] de igual Ø que contiene el nivel (las divisiones no son paredes)
function contRunAt(idx) {
    var xs = shaftBounds(), d = shaftLevels[idx].d, first = idx, last = idx;
    while (first > 0 && Math.abs(shaftLevels[first - 1].d - d) < 1e-9) first--;
    while (last < shaftLevels.length - 1 && Math.abs(shaftLevels[last + 1].d - d) < 1e-9) last++;
    return [xs[first], xs[last + 1]];
}
// leg s (r o c) del grupo cuyo vértice consume el anillo (frontera bnd, Ø d); 0 = anillo
// intacto (espejo de ShaftSpec.CornerOperationSizeAtRingMm del host)
function cornerOpSizeAt(bnd, d) {
    function scan(groups, key) {
        for (var i = 0; i < groups.length; i++) {
            var g = groups[i], s = g[key];
            if (!g.edges || !(s > 0)) continue;
            for (var e = 0; e < g.edges.length; e++) {
                var cid = g.edges[e], lvl = cornerLvl(cid);
                if (!(cid >= 0) || lvl < 0 || lvl >= shaftLevels.length) continue;
                if (cornerBnd(cid) === bnd && Math.abs(shaftLevels[lvl].d - d) < 1e-9) return s;
            }
        }
        return 0;
    }
    return scan(shaftFils, 'r') || scan(shaftChms, 'c');
}
// el vértice ya lo usa OTRO grupo (excluye la copia en edición): bloquea el clic
function cornerTaken(cid) {
    for (var i = 0; i < shaftFils.length; i++) {
        if (editFil && i === editFilIdx) continue;
        if (shaftFils[i].edges.indexOf(cid) >= 0) return true;
    }
    for (var j = 0; j < shaftChms.length; j++) {
        if (editChm && j === editChmIdx) continue;
        if (shaftChms[j].edges.indexOf(cid) >= 0) return true;
    }
    return false;
}
// vértices de TODOS los grupos, con el grupo en edición sustituyendo a su original
// (own = pertenece al grupo que se está validando)
function cornerOpsFor(cand, candIdx, candCh) {
    var ops = [];
    function pushG(g, ch, name, own) {
        if (!g || !g.edges) return;
        var s = ch ? g.c : g.r;
        for (var e = 0; e < g.edges.length; e++) ops.push({ cid: g.edges[e], s: s, ch: ch, name: name, own: own });
    }
    for (var i = 0; i < shaftFils.length; i++) {
        var selfF = cand && !candCh && i === candIdx;
        pushG(selfF ? cand : shaftFils[i], false, 'el redondeo ' + (i + 1), selfF);
    }
    if (cand && !candCh && candIdx === -1) pushG(cand, false, 'el redondeo nuevo', true);
    for (var j = 0; j < shaftChms.length; j++) {
        var selfC = cand && candCh && j === candIdx;
        pushG(selfC ? cand : shaftChms[j], true, 'el chaflán ' + (j + 1), selfC);
    }
    if (cand && candCh && candIdx === -1) pushG(cand, true, 'el chaflán nuevo', true);
    return ops;
}
// null = válida; comprobación completa de UN vértice del grupo (espejo de
// ShaftSpec.ValidateCornerEdge del host)
function cornerErr(cid, s, ops) {
    var n = shaftLevels.length, xs = shaftBounds();
    if (!(cid >= 0 && cid <= 2 * n - 1)) return 'Vértice no válido (¿cambiaste los niveles?).';
    var lvl = cornerLvl(cid), bnd = cornerBnd(cid);
    if (!cornerExists(cid)) return 'El vértice en x = ' + fmt(xs[bnd]) + ' ya no es una esquina (Ø iguales).';
    var isEnd = cornerIsEnd(cid), dLvl = shaftLevels[lvl].d;
    if (!(dLvl > 0)) return 'El nivel del vértice no es válido.';
    // encaje radial: en el extremo el cateto baja por la cara (s < r); en un hombro
    // recorre la pared del escalón, cóncavo hacia arriba / convexo hacia abajo (s < h)
    var h = cornerH(cid);
    if (isEnd) {
        if (!(s < dLvl / 2 - KTOL)) return 'La medida (' + fmt(s) + ') no cabe en el radio del extremo (Ø' + fmt(dLvl) + ').';
    } else {
        if (!(s < h - KTOL)) return 'La medida (' + fmt(s) + ') no cabe en la altura del hombro (' + fmt(h) + ' mm).';
    }
    var z = cornerZone(cid, s), ownDup = 0;
    // una operación por vértice; los dos vértices del MISMO hombro comparten pared
    // (s + s' < h); y sin solape INTERIOR de zonas con los demás vértices (dos zonas
    // pueden tocarse — superficies distintas se encuentran justo en la frontera)
    for (var i = 0; i < ops.length; i++) {
        var o = ops[i];
        if (o.own && o.cid === cid) { ownDup++; continue; }
        if (o.cid === cid) return 'Ese vértice ya lo usa ' + o.name + '.';
        if (!(o.cid >= 0 && o.cid <= 2 * n - 1) || !(o.s > 0) || !cornerExists(o.cid)) continue;
        if (h > 0 && cornerBnd(o.cid) === bnd && cornerLvl(o.cid) !== lvl) {
            // vértice opuesto del mismo hombro: ambos recorren la misma pared
            if (!(s + o.s < h - KTOL)) return 'No cabe junto a ' + o.name + ' en la altura del hombro (' + fmt(h) + ' mm).';
            continue;
        }
        var oz = cornerZone(o.cid, o.s);
        if (z[0] < oz[1] - KTOL && z[1] > oz[0] + KTOL) return 'Se solapa con ' + o.name + '.';
    }
    if (ownDup > 1) return 'El vértice en x = ' + fmt(xs[bnd]) + ' está repetido en el grupo.';
    // una entalladura SUSTITUYE la esquina CÓNCAVA (nivel menor) del hombro: solo ese
    // vértice queda bloqueado; el convexo (nivel mayor) sigue libre para chaflán/redondeo
    if (!isEnd) {
        for (var u = 0; u < shaftUcs.length; u++) {
            var us = shaftUcs[u];
            if (us.bnd === bnd && (ucSmallLeft(us) ? us.bnd - 1 : us.bnd) === lvl) {
                return 'Ya hay una entalladura en ese hombro.';
            }
        }
    }
    // encaje axial: el extremo lejano de la zona debe quedar estrictamente dentro del
    // tramo continuo de la superficie del nivel
    var run = contRunAt(lvl);
    var fits = cornerSide(cid) === 0 ? z[1] < run[1] - KTOL : z[0] > run[0] + KTOL;
    if (!fits) return 'La medida (' + fmt(s) + ' mm) no cabe en el tramo de Ø' + fmt(dLvl) + '.';
    // sin solape (ni toque) con entalladuras SOLO en la misma superficie (mismo Ø): la del
    // vértice convexo toca la zona de la entalladura justo en la frontera pero vive en el
    // cilindro mayor — no interfieren
    for (var u2 = 0; u2 < shaftUcs.length; u2++) {
        var uv = shaftUcs[u2];
        if (!(uv.bnd >= 1 && uv.bnd <= n - 1)) continue;
        var sp = ucSpan(uv);
        if (Math.abs(sp[2] - dLvl) >= 1e-9) continue;
        if (z[0] < sp[1] + KTOL && z[1] > sp[0] - KTOL) return 'Se solapa con la entalladura ' + (u2 + 1) + '.';
    }
    for (var g = 0; g < shaftGrvs.length; g++) {
        var g1 = grvX1(shaftGrvs[g]), g2 = g1 + shaftGrvs[g].e1;
        if (isFinite(g1) && z[0] < g2 + KTOL && z[1] > g1 - KTOL) return 'Se solapa con la ranura de anillo ' + (g + 1) + '.';
    }
    // las chavetas NO bloquean: el chaflán/redondeo se aplica ANTES del corte de chaveta
    // y pueden solaparse (la chaveta atraviesa el chaflán, resultado válido en taller)
    // en un extremo debe quedar cara plana alrededor de la boca del punto de centrado
    if (isEnd) {
        var endNo = cornerSide(cid) === 0 ? 0 : 1;
        for (var c2 = 0; c2 < shaftChs.length; c2++) {
            if (shaftChs[c2].end !== endNo) continue;
            if (!(chMouth(shaftChs[c2]) / 2 < dLvl / 2 - s - KTOL)) {
                return 'No deja cara plana para el punto de centrado ' + (c2 + 1) + '.';
            }
        }
    }
    // Las roscas cosméticas NO bloquean el vértice (un extremo roscado casi siempre lleva
    // chaflán): el host re-ancla la rosca al anillo nuevo de la operación, s hacia dentro.
    // Único límite: la operación no puede comerse la rosca ENTERA (s < profundidad útil).
    for (var t = 0; t < shaftThs.length; t++) {
        var tv = shaftThs[t];
        if (!(tv.lvl >= 0 && tv.lvl < n)) continue;
        var ab = tv.side === 1 ? tv.lvl + 1 : tv.lvl;
        if (ab === bnd && Math.abs(shaftLevels[tv.lvl].d - dLvl) < 1e-9) {
            var tLen = tv.depth > 0 ? tv.depth : shaftLevels[tv.lvl].l;
            if (!(s < tLen - KTOL)) return 'La medida (' + fmt(s) + ' mm) cubre toda la rosca ' + (t + 1) + '.';
        }
    }
    return null;
}
// null = válido; espejo de ShaftSpec.ValidateFillet del host
function validateFil(f, idx) {
    if (!(f.r > 0)) return 'El radio debe ser mayor que 0.';
    if (!f.edges.length) return 'Haz clic en al menos un vértice rojo del dibujo.';
    var ops = cornerOpsFor(f, idx, false);
    for (var e = 0; e < f.edges.length; e++) {
        var msg = cornerErr(f.edges[e], f.r, ops);
        if (msg !== null) return msg;
    }
    return null;
}
// null = válido; espejo de ShaftSpec.ValidateChamfer del host
function validateChm(c, idx) {
    if (!(c.c > 0)) return 'La longitud debe ser mayor que 0.';
    if (!c.edges.length) return 'Haz clic en al menos un vértice rojo del dibujo.';
    var ops = cornerOpsFor(c, idx, true);
    for (var e = 0; e < c.edges.length; e++) {
        var msg = cornerErr(c.edges[e], c.c, ops);
        if (msg !== null) return msg;
    }
    return null;
}
// primer grupo aceptado que dejó de ser válido; null = todos bien
function shaftFirstBadFil() {
    for (var i = 0; i < shaftFils.length; i++) {
        var m = validateFil(shaftFils[i], i);
        if (m !== null) return 'Redondeo ' + (i + 1) + ': ' + m;
    }
    return null;
}
function shaftFirstBadChm() {
    for (var i = 0; i < shaftChms.length; i++) {
        var m = validateChm(shaftChms[i], i);
        if (m !== null) return 'Chaflán ' + (i + 1) + ': ' + m;
    }
    return null;
}

// añade/quita un vértice del grupo (clic en el dibujo); el simétrico es el mismo id
function toggleCornerEdge(g, cid) {
    var at = g.edges.indexOf(cid);
    if (at < 0) g.edges.push(cid); else g.edges.splice(at, 1);
    g.edges.sort(function (a, b) { return a - b; });
}
function updateCornerNote() {
    if (editFil) {
        filNoteEl.textContent = editFil.edges.length
            ? 'Radio ' + fmt(editFil.r || 0) + ' mm en ' + editFil.edges.length + ' vértice(s). Vuelve a hacer clic para quitarlo.'
            : 'Haz clic en los vértices rojos del dibujo (el simétrico es el mismo anillo). Un grupo comparte el radio; para otro radio crea otro redondeo.';
        filNoteEl.className = 'note';
    } else if (editChm) {
        chmNoteEl.textContent = editChm.edges.length
            ? 'Chaflán ' + fmt(editChm.c || 0) + ' × 45° en ' + editChm.edges.length + ' vértice(s). Vuelve a hacer clic para quitarlo.'
            : 'Haz clic en los vértices rojos del dibujo (el simétrico es el mismo anillo). Un grupo comparte la longitud; para otra longitud crea otro chaflán.';
        chmNoteEl.className = 'note';
    }
}

btnAddFil.addEventListener('click', function () {
    editFil = { r: 1, edges: [] };
    editFilIdx = -1;
    openFilForm(); shaftUpdate();
});
btnFilOk.addEventListener('click', function () {
    if (!editFil || validateFil(editFil, editFilIdx) !== null) return;
    if (editFilIdx >= 0) shaftFils[editFilIdx] = editFil; else shaftFils.push(editFil);
    closeFilForm(); shaftUpdate();
});
btnFilDel.addEventListener('click', function () {
    if (editFilIdx >= 0) shaftFils.splice(editFilIdx, 1);
    closeFilForm(); shaftUpdate();
});
fradEl.addEventListener('input', function () {
    if (!editFil) return;
    editFil.r = parseFloat(fradEl.value);
    shaftUpdate();
});
btnAddChm.addEventListener('click', function () {
    editChm = { c: 1, edges: [] };
    editChmIdx = -1;
    openChmForm(); shaftUpdate();
});
btnChmOk.addEventListener('click', function () {
    if (!editChm || validateChm(editChm, editChmIdx) !== null) return;
    if (editChmIdx >= 0) shaftChms[editChmIdx] = editChm; else shaftChms.push(editChm);
    closeChmForm(); shaftUpdate();
});
btnChmDel.addEventListener('click', function () {
    if (editChmIdx >= 0) shaftChms.splice(editChmIdx, 1);
    closeChmForm(); shaftUpdate();
});
clenEl.addEventListener('input', function () {
    if (!editChm) return;
    editChm.c = parseFloat(clenEl.value);
    shaftUpdate();
});

function openFilForm() {
    fradEl.value = editFil.r;
    updateCornerNote();
    filFormEl.classList.remove('hidden');
    btnAddFil.classList.add('hidden');
    btnFilDel.style.display = editFilIdx >= 0 ? '' : 'none';
    renderFilList();
}
function closeFilForm() {
    editFil = null; editFilIdx = -1;
    filFormEl.classList.add('hidden');
    btnAddFil.classList.remove('hidden');
    renderFilList();
}
function openChmForm() {
    clenEl.value = editChm.c;
    updateCornerNote();
    chmFormEl.classList.remove('hidden');
    btnAddChm.classList.add('hidden');
    btnChmDel.style.display = editChmIdx >= 0 ? '' : 'none';
    renderChmList();
}
function closeChmForm() {
    editChm = null; editChmIdx = -1;
    chmFormEl.classList.add('hidden');
    btnAddChm.classList.remove('hidden');
    renderChmList();
}
function cornerEdgesLabel(edges) {
    var xs = shaftBounds(), pos = [];
    for (var e = 0; e < edges.length; e++) {
        var cid = edges[e], lvl = Math.max(0, Math.min(cornerLvl(cid), shaftLevels.length - 1));
        var b = Math.max(0, Math.min(cornerBnd(cid), xs.length - 1));
        pos.push(fmt(xs[b]) + '·Ø' + fmt(shaftLevels[lvl].d));
    }
    return pos.join(', ');
}
function renderFilList() {
    var html = '';
    for (var i = 0; i < shaftFils.length; i++) {
        var f = shaftFils[i];
        html += '<div class="keyitem' + (i === editFilIdx ? ' editing' : '') + '" data-fil="' + i + '">' +
            '<span>' + (i + 1) + ' · r ' + fmt(f.r) + ' · ' + f.edges.length + ' vértice(s) · x ' + cornerEdgesLabel(f.edges) + '</span>' +
            '<span class="kx" data-fdel="' + i + '">✕</span></div>';
    }
    filListEl.innerHTML = html;
    var items = filListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-fdel');
            if (del !== null && del !== undefined) {
                shaftFils.splice(parseInt(del, 10), 1);
                closeFilForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editFilIdx = parseInt(this.getAttribute('data-fil'), 10);
            editFil = JSON.parse(JSON.stringify(shaftFils[editFilIdx]));
            openFilForm(); shaftUpdate();
        });
    }
}
function renderChmList() {
    var html = '';
    for (var i = 0; i < shaftChms.length; i++) {
        var c = shaftChms[i];
        html += '<div class="keyitem' + (i === editChmIdx ? ' editing' : '') + '" data-chm="' + i + '">' +
            '<span>' + (i + 1) + ' · c ' + fmt(c.c) + ' × 45° · ' + c.edges.length + ' vértice(s) · x ' + cornerEdgesLabel(c.edges) + '</span>' +
            '<span class="kx" data-hdel="' + i + '">✕</span></div>';
    }
    chmListEl.innerHTML = html;
    var items = chmListEl.querySelectorAll('.keyitem');
    for (var j = 0; j < items.length; j++) {
        items[j].addEventListener('click', function (e) {
            var del = e.target.getAttribute && e.target.getAttribute('data-hdel');
            if (del !== null && del !== undefined) {
                shaftChms.splice(parseInt(del, 10), 1);
                closeChmForm(); shaftUpdate();
                e.stopPropagation();
                return;
            }
            editChmIdx = parseInt(this.getAttribute('data-chm'), 10);
            editChm = JSON.parse(JSON.stringify(shaftChms[editChmIdx]));
            openChmForm(); shaftUpdate();
        });
    }
}

// ---- render / estado ------------------------------------------------------

// coletilla de cada paso interno para la línea sub (el "Paso X de N" se antepone según el modo)
var STEP_SUBS = {
    1: 'niveles de izquierda a derecha · un nivel = Ø × L',
    2: 'chavetas (forma A) · elige arista, cota ±, profundidad, ángulo y nº',
    3: 'ranuras de anillo (DIN 471) · elige arista, cota ±, E1 y D3',
    4: 'entalladuras (DIN 509) · elige hombro, tipo y solicitación',
    5: 'puntos de centrado (DIN 332) · elige extremo, tipo y tamaño',
    6: 'roscas cosméticas · elige nivel, paso y alcance',
    7: 'redondeos · un radio por grupo · marca hombros o extremos',
    8: 'chaflanes a 45° · una longitud por grupo · marca hombros o extremos'
};

function shaftRender() {
    var seq = wizSteps(), pos = seq.indexOf(shaftStep);
    var isBolt = wizMode === 'B';

    document.getElementById('shaftTitle').textContent = isBolt ? 'BULON' : 'EJE';
    for (var g = 1; g <= 8; g++) {
        document.getElementById('sgrp' + g).classList.toggle('hidden', shaftStep !== g);
    }
    // puntos de progreso: los seq.length primeros representan los pasos visibles; el resto se oculta
    // (con sus barras) via la clase short del contenedor (CSS: .steps.short esconde n+... hijos)
    document.getElementById('shaftSteps').className = 'steps' + (isBolt ? ' short' : '');
    for (var d = 1; d <= 8; d++) {
        var dot = document.getElementById('sdot' + d), i = d - 1;
        dot.className = 'dot ' + (i === pos ? 'active' : i < pos ? 'done' : '');
    }
    // los pasos del cuerpo, ranuras y chaflanes se renumeran según el modo
    document.getElementById('sgrp1Title').textContent = isBolt
        ? 'Paso 1 · Cabeza y vástago' : 'Paso 1 · Cuerpo (niveles)';
    document.getElementById('sgrp3Title').textContent =
        'Paso ' + (seq.indexOf(3) + 1) + ' · Ranuras';
    document.getElementById('sgrp8Title').textContent =
        'Paso ' + (seq.indexOf(8) + 1) + ' · Chaflanes (45°)';
    // en modo bulón el nº de niveles es fijo (2): el campo no se muestra
    document.getElementById('shaftNWrap').classList.toggle('hidden', isBolt);

    shaftBackBtn.textContent = pos === 0 ? 'Volver' : 'Atrás';
    shaftNextBtn.textContent = pos === seq.length - 1 ? 'Crear' : 'Siguiente';
    shaftSubEl.textContent = 'Paso ' + (pos + 1) + ' de ' + seq.length + ' · ' +
        (isBolt && shaftStep === 1 ? 'cabeza Ø1 × L1 y vástago Ø2 × L2 · Ø2 debe ser menor que Ø1'
            : STEP_SUBS[shaftStep]);
    if (shaftStep === 2) renderKeyList();
    if (shaftStep === 3) renderGrvList();
    if (shaftStep === 4) renderUcList();
    if (shaftStep === 5) renderChList();
    if (shaftStep === 6) renderThList();
    if (shaftStep === 7) renderFilList();
    if (shaftStep === 8) renderChmList();
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
                shaftErrEl.textContent = shaftGrvs.length + ' ranura(s) · siguiente: ' +
                    (wizMode === 'B' ? 'chaflanes' : 'entalladuras');
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
    } else if (shaftStep === 5) {
        if (editCh) {
            var cmsg = validateCh(editCh, editChIdx);
            btnChOk.disabled = cmsg !== null;
            shaftNextBtn.disabled = true;
            if (cmsg) { shaftErrEl.textContent = cmsg; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Punto ' + editCh.form + (chIsThreaded(editCh.form) ? ' M' + fmt(editCh.d1) : ' d1 ' + fmt(editCh.d1)) +
                    ' · ' + (editCh.end === 0 ? 'extremo izq.' : 'extremo der.') +
                    ' · boca Ø' + fmt(chMouth(editCh)) + ' · prof. ' + fmt(chDepth(editCh)) + ' mm';
            }
        } else {
            var cbad = shaftFirstBadCh();
            if (cbad) { shaftErrEl.textContent = cbad + ' (edítalo o bórralo)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftChs.length + ' punto(s) de centrado · siguiente: roscas cosméticas';
            }
            shaftNextBtn.disabled = cbad !== null;
        }
        drawChSection();
    } else if (shaftStep === 6) {
        if (editTh) {
            var tmsg = validateTh(editTh, editThIdx);
            btnThOk.disabled = tmsg !== null;
            shaftNextBtn.disabled = true;
            if (tmsg) { shaftErrEl.textContent = tmsg; }
            else {
                var tz = thSpan(editTh);
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Rosca M' + fmt(shaftLevels[editTh.lvl].d) + '×' + fmt(editTh.pitch) +
                    ' · x ' + fmt(tz[0]) + '…' + fmt(tz[1]) + ' mm · núcleo Ø' +
                    fmt(thMinor(shaftLevels[editTh.lvl].d, editTh.pitch));
            }
        } else {
            var tbad = shaftFirstBadTh();
            if (tbad) { shaftErrEl.textContent = tbad + ' (edítala o bórrala)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftThs.length + ' rosca(s) cosmética(s) · siguiente: redondeos';
            }
            shaftNextBtn.disabled = tbad !== null;
        }
    } else if (shaftStep === 7) {
        if (editFil) {
            var fmsg = validateFil(editFil, editFilIdx);
            btnFilOk.disabled = fmsg !== null;
            shaftNextBtn.disabled = true;
            updateCornerNote();
            if (fmsg) { shaftErrEl.textContent = fmsg; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Redondeo r ' + fmt(editFil.r) + ' mm · ' + editFil.edges.length + ' vértice(s)';
            }
        } else {
            var fbad = shaftFirstBadFil();
            if (fbad) { shaftErrEl.textContent = fbad + ' (edítalo o bórralo)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftFils.length + ' redondeo(s) · siguiente: chaflanes';
            }
            shaftNextBtn.disabled = fbad !== null;
        }
    } else {
        if (editChm) {
            var hmsg = validateChm(editChm, editChmIdx);
            btnChmOk.disabled = hmsg !== null;
            shaftNextBtn.disabled = true;
            updateCornerNote();
            if (hmsg) { shaftErrEl.textContent = hmsg; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = 'Chaflán c ' + fmt(editChm.c) + ' mm × 45° · ' + editChm.edges.length + ' vértice(s)';
            }
        } else {
            var hbad = shaftFirstBadChm();
            if (hbad) { shaftErrEl.textContent = hbad + ' (edítalo o bórralo)'; }
            else {
                shaftErrEl.className = 'err ok';
                shaftErrEl.textContent = shaftChms.length + ' chaflán(es) · listo para crear';
            }
            shaftNextBtn.disabled = hbad !== null;
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
            last.b2 = i3 + 1;
        } else {
            segs.push({ x1: (last ? last.x1 + last.len : 0), len: lv[i3].l, d: lv[i3].d, b1: i3, b2: i3 + 1 });
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
    // las entalladuras también COMEN la silueta de la superficie menor (su muesca DIN 509
    // ya dibuja el contorno real): solo el tramo sobre el cilindro menor, NO la extensión
    // t2 de la forma F (esa vive en la cara del hombro, bajo la silueta mayor)
    var eatUcs = shaftUcs.slice();
    if (editUc) eatUcs.push(editUc);
    for (var ue = 0; ue < eatUcs.length; ue++) {
        if (editUc && ue === editUcIdx) continue;
        var uvE = eatUcs[ue];
        if (!(uvE.bnd >= 1 && uvE.bnd <= lv.length - 1) || !(uvE.f > 0) || !(uvE.t1 > 0)) continue;
        if (Math.abs(lv[uvE.bnd - 1].d - lv[uvE.bnd].d) < 1e-9) continue;
        var uz = ucZone(uvE);
        eat.push([uz[0], uz[1]]);
    }

    // vértices con redondeo/chaflán (aceptados y el grupo en edición): su zona axial
    // también COME la silueta, para que la diagonal o el arco sustituyan a la esquina
    // y las aristas nuevas queden CONECTADAS al contorno. Id de vértice = 2·nivel + lado.
    var bxs = [0];
    for (var bx2 = 0; bx2 < lv.length; bx2++) bxs.push(bxs[bx2] + lv[bx2].l);
    // elegibilidad sobre la geometría saneada del dibujo
    function cornerOkDraw(cid) {
        var lvl2 = cid >> 1, side2 = cid & 1;
        if (cid < 0 || lvl2 >= lv.length) return false;
        if (side2 === 0) return lvl2 === 0 || Math.abs(lv[lvl2 - 1].d - lv[lvl2].d) >= 1e-9;
        return lvl2 === lv.length - 1 || Math.abs(lv[lvl2 + 1].d - lv[lvl2].d) >= 1e-9;
    }
    var cornerOps = [];
    function pushCornerG(g, ch, isEdit) {
        if (!g || !g.edges) return;
        var sG = ch ? g.c : g.r;
        if (!(sG > 0)) return;
        for (var ce = 0; ce < g.edges.length; ce++) {
            var cG = g.edges[ce];
            if (!cornerOkDraw(cG)) continue;
            cornerOps.push({ cid: cG, lvl: cG >> 1, side: cG & 1, s: sG, ch: ch, edit: isEdit });
        }
    }
    for (var fg = 0; fg < shaftFils.length; fg++) { if (editFil && fg === editFilIdx) continue; pushCornerG(shaftFils[fg], false, false); }
    if (editFil) pushCornerG(editFil, false, true);
    for (var cg = 0; cg < shaftChms.length; cg++) { if (editChm && cg === editChmIdx) continue; pushCornerG(shaftChms[cg], true, false); }
    if (editChm) pushCornerG(editChm, true, true);
    // por frontera puede haber DOS operaciones (vértice cóncavo + convexo del hombro)
    var opsByBnd = {};
    for (var ob = 0; ob < cornerOps.length; ob++) {
        var opB = cornerOps[ob];
        var bndB = opB.lvl + opB.side;
        (opsByBnd[bndB] = opsByBnd[bndB] || []).push(opB);
        // zona axial: s hacia dentro del nivel desde su borde → interrumpe la silueta
        eat.push(opB.side === 0
            ? [bxs[opB.lvl], bxs[opB.lvl] + opB.s]
            : [bxs[opB.lvl + 1] - opB.s, bxs[opB.lvl + 1]]);
    }

    // vertical de frontera de un tramo, recortada donde las esquinas la consumen: en un
    // extremo la cara pierde s arriba y abajo; en un hombro, la op del vértice CONVEXO
    // (Ø mayor) quita la banda exterior [rB−s, rB] de la pared, y la del CÓNCAVO
    // (Ø menor) quita toda la banda central hasta rS+s (su anillo desaparece: la
    // arista nueva se dibuja aparte)
    function cornerVert(bnd, dSeg, xpx) {
        var rSeg = dSeg * sc / 2;
        var ops2 = opsByBnd[bnd], runsV = [[-rSeg, rSeg]];
        if (ops2) {
            var isEndB = bnd === 0 || bnd === lv.length;
            var cut = [];   // bandas [y1, y2] (px de radio, con signo) a eliminar
            for (var oi = 0; oi < ops2.length; oi++) {
                var op = ops2[oi], rl = lv[op.lvl].d * sc / 2, spy = op.s * sc;
                if (isEndB) {
                    cut.push([rl - spy, rl], [-rl, -(rl - spy)]);
                } else {
                    var dS2 = Math.min(lv[bnd - 1].d, lv[bnd].d) * sc / 2;
                    var dB2 = Math.max(lv[bnd - 1].d, lv[bnd].d) * sc / 2;
                    if (Math.abs(rl - dB2) < 1e-6) cut.push([dB2 - spy, dB2], [-dB2, -(dB2 - spy)]);   // convexo
                    else cut.push([-(dS2 + spy), dS2 + spy]);                                          // cóncavo
                }
            }
            for (var ci2 = 0; ci2 < cut.length; ci2++) {
                var nr2 = [];
                for (var ri2 = 0; ri2 < runsV.length; ri2++) {
                    var a2 = runsV[ri2][0], b2 = runsV[ri2][1], c0 = cut[ci2][0], c1 = cut[ci2][1];
                    if (c1 <= a2 || c0 >= b2) { nr2.push([a2, b2]); continue; }
                    if (c0 > a2) nr2.push([a2, c0]);
                    if (c1 < b2) nr2.push([c1, b2]);
                }
                runsV = nr2;
            }
        }
        var o2 = '';
        for (var rv = 0; rv < runsV.length; rv++) {
            if (runsV[rv][1] - runsV[rv][0] < 0.5) continue;
            o2 += LINE(xpx, cy + runsV[rv][0], xpx, cy + runsV[rv][1], INK, 1.5);
        }
        return o2;
    }

    // contorno por tramo fundido (sin aristas internas): verticales completas y
    // silueta superior/inferior interrumpida donde una ranura la come
    for (var s2 = 0; s2 < segs.length; s2++) {
        var sg = segs[s2];
        var sxA = x0 + sg.x1 * sc, sxB = sxA + sg.len * sc;
        var syT = cy - sg.d * sc / 2, syB = cy + sg.d * sc / 2;
        out += cornerVert(sg.b1, sg.d, sxA) + cornerVert(sg.b2, sg.d, sxB);
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
        // las paredes son caras anulares vistas de canto: la arista cruza el eje de lado a
        // lado — tramo central que une las dos muescas
        out += LINE(ga, cy - rB, ga, cy + rB, gcol, gw) + LINE(gb, cy - rB, gb, cy + rB, gcol, gw);
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
            // aristas circulares de la revolución: verticales que unen las dos siluetas
            // (arranque de rampa, fin de rampa y fin de fondo; el hombro ya tiene su vertical)
            out += LINE(xfar, cy - urs, xfar, cy + urs, ucol, uw);
            out += LINE(xrmp, cy - urb, xrmp, cy + urb, ucol, uw) + LINE(xd, cy - urb, xd, cy + urb, ucol, uw);
        } else {
            out += PATH([[xfar, cy - urs], [xrmp, cy - urb], [xsh, cy - urb], [xsh, cy - urs]], ucol, uw);
            out += PATH([[xfar, cy + urs], [xrmp, cy + urb], [xsh, cy + urb], [xsh, cy + urs]], ucol, uw);
            out += LINE(xfar, cy - urs, xfar, cy + urs, ucol, uw) + LINE(xrmp, cy - urb, xrmp, cy + urb, ucol, uw);
        }
    }

    // puntos de centrado aceptados (y el editado en rojo): semiperfil taladrado en la cara del
    // extremo, espejado arriba/abajo (boca → punta sobre el eje)
    var allChs = shaftChs.slice();
    if (editCh) allChs.push(editCh);
    for (var ci = 0; ci < allChs.length; ci++) {
        if (editCh && ci === editChIdx) continue;   // en edición se dibuja la copia roja, no el original
        var cv = allChs[ci];
        if (!(chMouth(cv) > 0) || !(chDepth(cv) > 0)) continue;
        var cEdit = editCh && ci === allChs.length - 1;
        var ccol = cEdit ? RED : INK, cw = cEdit ? 1.8 : 1.2;
        var pp = chProfilePts(cv), topArr = [], botArr = [];
        for (var pi = 0; pi < pp.length; pi++) {
            topArr.push([x0 + pp[pi].x * sc, cy - pp[pi].r * sc]);
            botArr.push([x0 + pp[pi].x * sc, cy + pp[pi].r * sc]);
        }
        out += PATH(topArr, ccol, cw) + PATH(botArr, ccol, cw);
        // aristas circulares del taladro: cada vértice REAL del perfil (no la teselación del
        // arco) es un círculo de la revolución → línea vertical que une los dos semiperfiles.
        // Cara (x local 0, ya la dibuja el contorno del tramo) y punta (r = 0) no llevan línea.
        var vv = chProfile(cv).v, xfC = cv.end === 0 ? 0 : total, sgC = cv.end === 0 ? 1 : -1;
        var vSeen = {};
        for (var vi = 0; vi < vv.length; vi++) {
            var lxV = vv[vi][0], rV = vv[vi][1];
            if (!(lxV > KTOL) || !(rV > KTOL)) continue;
            var kV = lxV.toFixed(4);                    // en un mismo x manda el radio mayor
            if (!(kV in vSeen) || rV > vSeen[kV]) vSeen[kV] = rV;
        }
        for (var kX in vSeen) {
            var xV = x0 + (xfC + sgC * parseFloat(kX)) * sc, rPx = vSeen[kX] * sc;
            out += LINE(xV, cy - rPx, xV, cy + rPx, ccol, cw);
        }
    }

    // roscas cosméticas aceptadas (y la editada en rojo): convenio de dibujo — línea FINA a
    // Ø de núcleo en ambas siluetas a lo largo de la rosca + límite fino donde termina
    var allThs = shaftThs.slice();
    if (editTh) allThs.push(editTh);
    for (var ti = 0; ti < allThs.length; ti++) {
        if (editTh && ti === editThIdx) continue;   // en edición se dibuja la copia roja, no la original
        var tv2 = allThs[ti];
        if (!(tv2.lvl >= 0 && tv2.lvl < shaftLevels.length) || !(tv2.pitch > 0)) continue;
        var tlv = shaftLevels[tv2.lvl];
        var trm = thMinor(tlv.d, tv2.pitch) / 2;
        if (!(trm > 0)) continue;
        var tspan = thSpan(tv2);
        if (!isFinite(tspan[0]) || !(tspan[1] > tspan[0])) continue;
        var tEdit = editTh && ti === allThs.length - 1;
        var tcol = tEdit ? RED : INK, tw2 = tEdit ? 1.2 : 0.8;
        var txa = x0 + tspan[0] * sc, txb = x0 + tspan[1] * sc, tyr = trm * sc;
        out += LINE(txa, cy - tyr, txb, cy - tyr, tcol, tw2) + LINE(txa, cy + tyr, txb, cy + tyr, tcol, tw2);
        // límite de rosca: en el extremo donde TERMINA (el arranque es el borde del nivel)
        var txe = tv2.side === 1 ? txa : txb;
        out += LINE(txe, cy - tyr, txe, cy + tyr, tcol, tw2);
    }

    // redondeos/chaflanes: la silueta y las verticales ya vienen recortadas — aquí se
    // dibuja la arista nueva en cada vértice: diagonal a 45° + su anillo nuevo (chaflán)
    // o arco tangente sin anillo (redondeo), arriba y abajo. En un vértice CONVEXO
    // (extremo o Ø mayor) la curva baja hacia el eje; en el CÓNCAVO (Ø menor) sube por
    // la pared del hombro.
    for (var co = 0; co < cornerOps.length; co++) {
        var op2 = cornerOps[co];
        var ccol2 = op2.edit ? RED : INK, cw3 = op2.edit ? 1.8 : 1.2;
        var spx = op2.s * sc;
        var bnd2d = op2.lvl + op2.side;
        var rL = lv[op2.lvl].d * sc / 2;
        var dirIn = op2.side === 0 ? 1 : -1;                 // hacia dentro del nivel
        var xB2 = x0 + bxs[bnd2d] * sc, xFar2 = xB2 + dirIn * spx;
        var isEnd2 = (op2.side === 0 && op2.lvl === 0) || (op2.side === 1 && op2.lvl === lv.length - 1);
        var convex = isEnd2 || lv[op2.lvl].d > lv[op2.side === 0 ? op2.lvl - 1 : op2.lvl + 1].d;
        if (convex && !(spx < rL)) continue;
        // sentido radial de la curva: convexo baja (radio − s), cóncavo sube (radio + s)
        var rTo = convex ? rL - spx : rL + spx;
        if (op2.ch) {
            out += LINE(xFar2, cy - rL, xB2, cy - rTo, ccol2, cw3) + LINE(xFar2, cy + rL, xB2, cy + rTo, ccol2, cw3);
            // anillo nuevo donde el cono corta el cilindro del nivel
            out += LINE(xFar2, cy - rL, xFar2, cy + rL, ccol2, cw3);
        } else {
            out += ARCC(xFar2, cy - rL, xB2, cy - rTo, xFar2, cy - rTo, ccol2, cw3);
            out += ARCC(xFar2, cy + rL, xB2, cy + rTo, xFar2, cy + rTo, ccol2, cw3);
        }
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
    } else if (editFil || editChm) {
        // vértices de esquina clicables: cada nivel expone sus 4 (arriba/abajo son el
        // MISMO anillo → mismo id y se marcan a la vez). Rojo hueco = libre, rojo
        // relleno = en el grupo (su preview ya se dibuja en rojo), gris = ocupado por
        // otro grupo (el clic se ignora). Círculo invisible ancho = zona de clic.
        var curFC = editFil || editChm;
        for (var cv2 = 0; cv2 < 2 * lv.length; cv2++) {
            if (!cornerOkDraw(cv2)) continue;
            var cvLvl = cv2 >> 1, cvSide = cv2 & 1;
            var cvx = x0 + bxs[cvLvl + cvSide] * sc, cvr = lv[cvLvl].d * sc / 2;
            var selV = curFC.edges.indexOf(cv2) >= 0;
            var takenV = !selV && cornerTaken(cv2);
            var vcol = takenV ? '#9AA0A6' : RED;
            // vértice superior e inferior (simétricos, mismo anillo)
            for (var vs2 = -1; vs2 <= 1; vs2 += 2) {
                var cvy = cy + vs2 * cvr;
                out += mk('circle', {
                    cx: n1(cvx), cy: n1(cvy), r: 4,
                    fill: selV ? RED : '#FFFFFF', 'fill-opacity': selV ? '1' : '0.01',
                    stroke: vcol, 'stroke-width': selV ? 2 : 1.6
                });
                out += mk('circle', {
                    cx: n1(cvx), cy: n1(cvy), r: 11,
                    fill: '#000000', 'fill-opacity': '0',
                    'pointer-events': 'all',
                    'class': 'edgehit', 'data-corner': cv2
                });
            }
        }
    } else if (editCh) {
        // punto de centrado en edición: cota de profundidad (axial, cara → punta) y Ø de
        // boca (radial en el carril del extremo), mismo estilo que las cotas del paso 1
        var chDep = chDepth(editCh), chMth = chMouth(editCh);
        if (chDep > 0 && chMth > 0) {
            var chXf = editCh.end === 0 ? 0 : total, chSign = editCh.end === 0 ? 1 : -1;
            var xFace = x0 + chXf * sc, xTip = x0 + (chXf + chSign * chDep) * sc;
            var rMouth = chMth * sc / 2;
            // profundidad: cota horizontal bajo la pieza (cara del extremo → punta sobre el eje)
            var yDep = boxBot + 24;
            out += LINE(xFace, cy + rMouth, xFace, yDep, INK, 1) + LINE(xTip, cy, xTip, yDep, INK, 1);
            out += hDim(Math.min(xFace, xTip), Math.max(xFace, xTip), yDep, 'prof. = ' + fmt(chDep));
            // Ø de boca: cota vertical en el carril del extremo (fuera de la pieza)
            var xQc = editCh.end === 0 ? x0 - 40 : x0 + total * sc + 40;
            out += LINE(xFace, cy - rMouth, xQc, cy - rMouth, INK, 1) + LINE(xFace, cy + rMouth, xQc, cy + rMouth, INK, 1);
            out += vDim(cy - rMouth, cy + rMouth, xQc, 'Ø boca ' + fmt(chMth));
        }
    } else if (editTh) {
        // niveles clicables: rect invisible sobre cada nivel + contorno azul en el elegido
        var xsT = shaftBounds();
        for (var li2 = 0; li2 < shaftLevels.length; li2++) {
            if (!(shaftLevels[li2].d > 0) || !(shaftLevels[li2].l > 0)) continue;
            var lx1 = x0 + xsT[li2] * sc, lx2 = x0 + xsT[li2 + 1] * sc;
            var lr2 = shaftLevels[li2].d * sc / 2;
            if (li2 === editTh.lvl) out += RECT(lx1, cy - lr2, lx2 - lx1, 2 * lr2, BLU, 2.2);
            out += mk('rect', {
                x: n1(lx1), y: n1(cy - lr2), width: n1(lx2 - lx1), height: n1(2 * lr2),
                fill: '#000000', 'fill-opacity': '0',
                'pointer-events': 'all',         // clicable aunque el relleno sea invisible
                'class': 'lvlhit', 'data-lvl': li2
            });
        }
        // rosca en edición: cota horizontal del tramo roscado bajo la pieza
        if (editTh.lvl >= 0 && editTh.lvl < shaftLevels.length && editTh.pitch > 0) {
            var tzD = thSpan(editTh);
            if (isFinite(tzD[0]) && tzD[1] > tzD[0]) {
                var tlvD = shaftLevels[editTh.lvl], trD = tlvD.d * sc / 2;
                var tx1D = x0 + tzD[0] * sc, tx2D = x0 + tzD[1] * sc, yTh = boxBot + 24;
                out += LINE(tx1D, cy + trD, tx1D, yTh, INK, 1) + LINE(tx2D, cy + trD, tx2D, yTh, INK, 1);
                out += hDim(tx1D, tx2D, yTh, 'rosca = ' + fmt(tzD[1] - tzD[0]));
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

// puntos del semiperfil del punto de centrado en coords locales (x = prof. desde la
// cara 0..profundidad, r = radio), con el arco teselado — espejo de chProfilePts sin el
// traslado al extremo del eje. Base del dibujo de la sección del paso 5.
function chProfileLocalPts(c) {
    var pr = chProfile(c), v = pr.v, out = [];
    for (var i = 0; i < v.length; i++) {
        if (i === pr.arcSeg && pr.arcR > 0) {
            var xa = v[i - 1][0], ya = v[i - 1][1], xb = v[i][0], yb = v[i][1];
            var ccx = xb, ccy = yb + pr.arcR;
            var a0 = Math.atan2(ya - ccy, xa - ccx), a1 = Math.atan2(yb - ccy, xb - ccx), N = 8;
            for (var k = 1; k <= N; k++) { var a = a0 + (a1 - a0) * k / N; out.push({ x: ccx + pr.arcR * Math.cos(a), r: ccy + pr.arcR * Math.sin(a) }); }
        } else out.push({ x: v[i][0], r: v[i][1] });
    }
    return out;
}

// TODAS las cotas de la forma, con su geometría (mismos cálculos que chProfile). dia = Ø
// (radio + etiqueta), de mayor a menor; len = profundidades desde una referencia axial x0.
function chDims(c) {
    var dia = [], len = [], note = '';
    if (chIsThreaded(c.form)) {
        var rc = c.d2 / 2, r3 = c.d3 / 2, r4 = c.d4 / 2, r5 = c.d5 / 2, s = c.form === 'DS' ? c.t5 : 0;
        if (c.form === 'DS') dia.push({ r: r5, name: 'd5', val: c.d5 });
        dia.push({ r: r4, name: 'd4', val: c.d4 });
        dia.push({ r: r3, name: 'd3', val: c.d3 });
        dia.push({ r: rc, name: 'd2', val: c.d2 });
        if (c.d1 / 2 > rc) dia.push({ r: c.d1 / 2, name: 'M' + fmt(c.d1), val: null, dash: true });
        // IS 2540:2008 Fig. 1-2: todas las profundidades se acotan desde la CARA (también en DS)
        if (c.form === 'DS') len.push({ x0: 0, x: s, name: 't5', val: c.t5 });
        len.push({ x0: 0, x: c.t4, name: 't4', val: c.t4 });
        len.push({ x0: 0, x: c.t3, name: 't3', val: c.t3 });
        len.push({ x0: 0, x: c.t1, name: 't1', val: c.t1 });
        len.push({ x0: 0, x: c.t2, name: 't2', val: c.t2 });
        if (c.form === 'DR') note = 'R = ' + fmt(c.rr);
    } else {
        var r1 = c.d1 / 2, r2 = c.d2 / 2, prot = 0;
        if (c.form === 'C') {
            var q4 = c.d4 / 2, q5 = c.d5 / 2; prot = (q5 - q4) / CH_TAN30;
            dia.push({ r: q5, name: 'd5', val: c.d5 }, { r: q4, name: 'd4', val: c.d4 }, { r: r2, name: 'd2', val: c.d2 }, { r: r1, name: 'd1', val: c.d1 });
            len.push({ x0: 0, x: prot, name: 'b', val: c.b });
        } else if (c.form === 'B') {
            var b3 = c.d3 / 2; prot = (b3 - r2) / CH_TAN60;
            dia.push({ r: b3, name: 'd3', val: c.d3 }, { r: r2, name: 'd2', val: c.d2 }, { r: r1, name: 'd1', val: c.d1 });
            len.push({ x0: 0, x: prot, name: 'b', val: c.b });
        } else {
            dia.push({ r: r2, name: 'd2', val: c.d2 }, { r: r1, name: 'd1', val: c.d1 });
            if (c.form === 'R') note = 'R = ' + fmt(chArcR(c));
        }
        len.push({ x0: 0, x: c.t, name: 't', val: c.t });
    }
    return { dia: dia, len: len, note: note };
}

// x (mm locales) donde el semiperfil alcanza el radio r: cruce más profundo de la polilínea
// (arranque de la línea de referencia de cada cota de Ø). Si r no se alcanza (p. ej. la
// cresta de rosca M, mayor que el núcleo dibujado) devuelve el cruce del chaflán o 0 (cara).
function chXAtR(pts, r) {
    var best = 0, found = false;
    for (var i = 1; i < pts.length; i++) {
        var r0 = pts[i - 1].r, r1 = pts[i].r;
        if (r0 === r1) continue;
        var t = (r - r0) / (r1 - r0);
        if (t < 0 || t > 1) continue;
        var x = pts[i - 1].x + (pts[i].x - pts[i - 1].x) * t;
        if (!found || x > best) { best = x; found = true; }
    }
    return best;
}

// sección axial del punto de centrado (paso 5) con TODAS las cotas: semiperfil taladrado en
// la cara del extremo, espejado; SOLO cotas horizontales y verticales — Ø como cotas
// verticales en carriles a la izquierda de la cara (menor más cerca, línea de referencia
// horizontal desde el punto real del perfil) y profundidades en filas horizontales debajo.
// Auto-escala a las medidas reales (como la sección de la chaveta).
function drawChSection() {
    if (!chSectionEl) return;
    if (!editCh) { chSectionEl.innerHTML = ''; return; }
    var c = editCh, D = chDepth(c), mouthR = chMouth(c) / 2;
    if (!(D > 0) || !(mouthR > 0)) { chSectionEl.innerHTML = ''; return; }
    var dims = chDims(c);
    dims.dia.sort(function (a, b) { return a.r - b.r; });   // Ø menor primero (carril más pegado)
    dims.len.sort(function (a, b) { return a.x - b.x; });   // profundidad menor primero (fila superior)

    // pie (se calcula antes para que el ancho del viewBox lo tenga en cuenta al centrar)
    var foot = 'DIN 332-' + (chIsThreaded(c.form) ? '2' : '1') + ' ' + c.form +
        (dims.note ? ' · ' + dims.note : '') + ' · prof. ' + fmt(D) + ' · boca Ø' + fmt(chMouth(c));

    var laneGap = 20, padT = 16, plotH = 104, plotW = 150;
    var laneW = 26 + (dims.dia.length - 1) * laneGap;   // carriles de Ø + valores, a la izquierda de la cara

    // radio del bloque = radio del extremo del eje (contexto), acotado para no aplastar el taladro
    var lvd = (c.end === 0 ? shaftLevels[0].d : shaftLevels[shaftLevels.length - 1].d) || (mouthR * 2.4);
    var blockR = Math.max(mouthR * 1.2, Math.min(lvd / 2, mouthR * 2.2));
    var s = Math.min(plotW / D, (plotH / 2 - 6) / blockR);

    // ancho real del contenido (carriles + bloque) → viewBox justo y dibujo CENTRADO en la caja
    var drawW = D * s + Math.min(16, plotW * 0.12) + 10;
    var contentW = laneW + drawW;
    var padB = 24 + dims.len.length * 15, H = padT + plotH + padB;
    var W = Math.max(contentW + 16, 12 + foot.length * 5.6);
    var x0 = laneW + (W - contentW) / 2;
    var axisY = padT + plotH / 2;
    function PX(x) { return x0 + x * s; }
    var bTop = axisY - blockR * s, bBot = axisY + blockR * s, xEnd = PX(D) + Math.min(16, plotW * 0.12);

    var pts = chProfileLocalPts(c), top = [], bot = [];
    for (var i = 0; i < pts.length; i++) { top.push([PX(pts[i].x), axisY - pts[i].r * s]); bot.push([PX(pts[i].x), axisY + pts[i].r * s]); }

    var out = LINE(x0 - 8, axisY, xEnd + 8, axisY, INK, 0.8, '7,3,2,3');   // eje
    // sólido: bordes del bloque + cara (interrumpida por la boca) + perfil del taladro (espejo)
    out += LINE(x0, bTop, xEnd, bTop, INK, 1.4) + LINE(xEnd, bTop, xEnd, bBot, INK, 1.4) + LINE(x0, bBot, xEnd, bBot, INK, 1.4);
    out += LINE(x0, bTop, x0, axisY - mouthR * s, INK, 1.4) + LINE(x0, bBot, x0, axisY + mouthR * s, INK, 1.4);
    out += PATH(top, INK, 1.4) + PATH(bot, INK, 1.4);
    // aristas circulares del taladro: cada vértice REAL del perfil es un círculo de la
    // revolución → vertical que une los dos semiperfiles (cara y punta no llevan línea)
    var vvS = chProfile(c).v, vSeenS = {};
    for (var vs = 0; vs < vvS.length; vs++) {
        var lxS = vvS[vs][0], rS2 = vvS[vs][1];
        if (!(lxS > KTOL) || !(rS2 > KTOL)) continue;
        var kS = lxS.toFixed(4);                    // en un mismo x manda el radio mayor
        if (!(kS in vSeenS) || rS2 > vSeenS[kS]) vSeenS[kS] = rS2;
    }
    for (var kSx in vSeenS) {
        var xVs = PX(parseFloat(kSx)), rPxS = vSeenS[kSx] * s;
        out += LINE(xVs, axisY - rPxS, xVs, axisY + rPxS, INK, 1.4);
    }

    // Ø: cotas VERTICALES en carriles a la izquierda de la cara, el Ø menor en el carril más
    // cercano (las líneas de referencia nunca cruzan una línea de cota). La línea de referencia
    // sale HORIZONTAL del punto real del perfil donde vive ese Ø.
    for (var d = 0; d < dims.dia.length; d++) {
        var e = dims.dia[d], laneX = x0 - 14 - d * laneGap;
        // solo el nombre (sin valor): los valores viven en los campos del detalle
        var lbl = e.val != null ? ('Ø' + e.name) : e.name;   // name ya es "M<d1>" en la rosca
        var edash = e.dash ? '4,3' : null, yT = axisY - e.r * s, yB = axisY + e.r * s;
        var xw = PX(chXAtR(pts, e.r));
        out += LINE(laneX - 4, yT, xw, yT, INK, 0.7, edash) + LINE(laneX - 4, yB, xw, yB, INK, 0.7, edash);
        if (yB - yT >= ARROW_FIT) out += LINE(laneX, yT, laneX, yB, INK, 1) + arrow(laneX, yT, 'u') + arrow(laneX, yB, 'd');
        else out += LINE(laneX, yT - DIM_EXT, laneX, yB + DIM_EXT, INK, 1) + arrow(laneX, yT, 'd') + arrow(laneX, yB, 'u');
        // valor junto al extremo SUPERIOR de la cota (no centrado en el eje): texto girado que
        // crece hacia abajo desde la flecha superior, a la izquierda de la línea de cota
        out += mk('text', {
            x: n1(laneX - 2), y: n1(yT + 8), fill: INK, 'text-anchor': 'end',
            'font-family': 'Segoe UI, sans-serif', 'font-size': '11',
            transform: 'rotate(-90 ' + n1(laneX - 2) + ' ' + n1(yT + 8) + ')'
        }, lbl);
    }
    // profundidades: filas de cota horizontales debajo del bloque (la más corta arriba).
    // Las líneas de referencia arrancan en el EJE de revolución (donde viven los puntos de
    // profundidad) y atraviesan el bloque; etiqueta solo con el nombre (valor en el detalle).
    for (var l = 0; l < dims.len.length; l++) {
        var g = dims.len[l], rowY2 = bBot + 16 + l * 15, xa = PX(g.x0), xb = PX(g.x);
        out += LINE(xa, axisY, xa, rowY2, INK, 0.8) + LINE(xb, axisY, xb, rowY2, INK, 0.8);
        out += hDim(xa, xb, rowY2, g.name);
    }
    // pie: norma, forma, cotas no dibujadas (R) y profundidad total
    out += TEXTL(6, H - 5, foot, '#5A6068');

    chSectionEl.innerHTML = mk('svg', { viewBox: '0 0 ' + W + ' ' + H, preserveAspectRatio: 'xMidYMid meet' }, out);
}

// ================================================================
// PRELOAD (re-edición): el host inyecta en window.PRELOAD el mensaje crudo
// guardado en el registro de piezas (create|shaft|wiz|...). Se parsea al
// estado del asistente (inverso EXACTO de shaftSubmit) y el asistente se abre
// directamente en el modo guardado con esos valores. Un mensaje malformado se
// ignora y se queda el catálogo. Campos solo-UI que el protocolo no lleva:
// din de las ranuras → false (E1/D3 quedan manuales, no se re-aplica la tabla),
// ser de las entalladuras → se infiere de la fila DIN 509 que case con r/t1/f,
// custom de los puntos de centrado → true (las cotas guardadas mandan).
function loadPreload(raw) {
    var p = String(raw).split('|');
    if (p[0] !== 'create' || p[1] !== 'shaft') return false;
    var wiz = p[2] === 'B' ? 'B' : p[2] === 'S' ? 'S' : null;
    if (!wiz) return false;
    var i = 3;
    function num() { var v = parseFloat(p[i++]); return isFinite(v) ? v : NaN; }
    function int() { var v = parseInt(p[i++], 10); return isFinite(v) ? v : NaN; }

    var n = int();
    if (!(n >= 1) || (wiz === 'B' && n !== 2)) return false;
    var lv = [];
    for (var k = 0; k < n; k++) {
        var d = num(), l = num();
        if (!isFinite(d) || !isFinite(l)) return false;
        lv.push({ d: d, l: l });
    }

    var nk = int(); if (!(nk >= 0)) return false;
    var keys = [];
    for (var a = 0; a < nk; a++) {
        var ky = { b: num(), l: num(), edge: int(), off: num(), depth: num(), refd: num(), ang: num(), cnt: int(), ctr: int() };
        if (!isFinite(ky.b) || !isFinite(ky.l) || !isFinite(ky.edge) || !isFinite(ky.off) ||
            !isFinite(ky.depth) || !isFinite(ky.refd) || !isFinite(ky.ang) || !isFinite(ky.cnt) || !isFinite(ky.ctr)) return false;
        keys.push(ky);
    }

    var ng = int(); if (!(ng >= 0)) return false;
    var grvs = [];
    for (var b = 0; b < ng; b++) {
        var gv = { e1: num(), d3: num(), edge: int(), off: num(), din: false };
        if (!isFinite(gv.e1) || !isFinite(gv.d3) || !isFinite(gv.edge) || !isFinite(gv.off)) return false;
        grvs.push(gv);
    }

    var nu = int(); if (!(nu >= 0)) return false;
    var ucs = [];
    for (var c = 0; c < nu; c++) {
        var u = { bnd: int(), form: p[i++], r: num(), t1: num(), f: num(), t2: num(), ser: 'usual' };
        if (!isFinite(u.bnd) || (u.form !== 'E' && u.form !== 'F') ||
            !isFinite(u.r) || !isFinite(u.t1) || !isFinite(u.f) || !isFinite(u.t2)) return false;
        for (var r9 = 0; r9 < DIN509.length; r9++) {
            var o = DIN509[r9];
            if (Math.abs(o.r - u.r) < 1e-9 && Math.abs(o.t1 - u.t1) < 1e-9 && Math.abs(o.f - u.f) < 1e-9) { u.ser = o.ser; break; }
        }
        ucs.push(u);
    }

    var nc = int(); if (!(nc >= 0)) return false;
    var chs = [];
    for (var e2 = 0; e2 < nc; e2++) {
        var cv = {
            end: int(), form: p[i++], custom: true, _key: 0,
            d1: num(), d2: num(), d3: num(), d4: num(), d5: num(),
            b: num(), rr: num(), t: num(), t1: num(), t2: num(), t3: num(), t4: num(), t5: num()
        };
        if (!isFinite(cv.end) || !isFinite(cv.d1) || !isFinite(cv.t)) return false;
        chs.push(cv);
    }

    var nt = int(); if (!(nt >= 0)) return false;
    var ths = [];
    for (var t9 = 0; t9 < nt; t9++) {
        var th = { lvl: int(), side: int(), pitch: num(), depth: num() };
        if (!isFinite(th.lvl) || !isFinite(th.side) || !isFinite(th.pitch) || !isFinite(th.depth)) return false;
        th.mode = th.depth > 0 ? 1 : 0;
        var lvd = lv[th.lvl] ? lv[th.lvl].d : 0, ps = thPitches(lvd), inList = false;
        for (var p9 = 0; p9 < ps.length; p9++) { if (Math.abs(ps[p9] - th.pitch) < 1e-9) inList = true; }
        th.customPitch = !inList;
        ths.push(th);
    }

    var nf = int(); if (!(nf >= 0)) return false;
    var fils = [];
    for (var f2 = 0; f2 < nf; f2++) {
        var fr = num(), fm = int();
        if (!isFinite(fr) || !(fm >= 1)) return false;
        var fe = [];
        for (var f3 = 0; f3 < fm; f3++) { var fv = int(); if (!isFinite(fv)) return false; fe.push(fv); }
        fils.push({ r: fr, edges: fe });
    }

    var nh = int(); if (!(nh >= 0)) return false;
    var chms = [];
    for (var h2 = 0; h2 < nh; h2++) {
        var hc = num(), hm = int();
        if (!isFinite(hc) || !(hm >= 1)) return false;
        var he = [];
        for (var h3 = 0; h3 < hm; h3++) { var hv = int(); if (!isFinite(hv)) return false; he.push(hv); }
        chms.push({ c: hc, edges: he });
    }
    if (i !== p.length) return false;

    wizMode = wiz;
    shaftLevels = lv; shaftKeys = keys; shaftGrvs = grvs; shaftUcs = ucs;
    shaftChs = chs; shaftThs = ths; shaftFils = fils; shaftChms = chms;
    shaftNEl.value = n;
    catalog.classList.add('hidden'); shaftSec.classList.remove('hidden');
    shaftStep = 1; shaftFocus = 0;
    buildLevelRows(); shaftRender();
    return true;
}
if (window.PRELOAD) { try { loadPreload(window.PRELOAD); } catch (e) { } }
