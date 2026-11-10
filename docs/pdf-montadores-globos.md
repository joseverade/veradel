# PDF para montadores (posición de globos)

**Problema:** en conjuntos grandes (A1/A0) con LDM largas es difícil encontrar en el plano los globos de una posición.

**Solución:** botón en dibujos que genera `<dibujo>_montaje.pdf`:

1. Las hojas del dibujo con las líneas de zona punteadas (0.1 mm, gris) y la etiqueta de cada zona ("A4") en la esquina superior izquierda de la celda.
2. Al final, páginas A4 con una copia de la LDM + columna **POSICIÓN** en formato `HOJA-ZONA` (`1-A4, 2-C3`), ordenada por hoja, letra y número. Cada posición es un enlace que encuadra sus globos en la hoja. Las filas sin globo muestran `SIN GLOBO`.

### Enlaces

`PdfPage.AddDocumentLink` de PdfSharp 1.50 escribe siempre `/Dest [pág /XYZ null null 0]`: abre la página pero conserva zoom y scroll → en un A0 no se ve el globo. Se probó `/FitR` (encuadre): las coordenadas eran correctas (225/225 verificados) pero Edge/Chrome lo ignoran. Solución: anotación propia (`ZoomLink : PdfAnnotation`) con `/Dest [pág /XYZ izquierda arriba zoom]`, que soportan Acrobat, Edge/Chrome y Firefox.

- Zoom fijo 2.0 (`FocusZoom`); se baja si los globos de esa zona no caben. Se divide por la escala de la página (`anchoPág / anchoHoja real`) para que el globo se vea igual en A0, A1…
- `/XYZ` pone (izquierda, arriba) en la esquina superior izquierda de la ventana y el visor no dice su tamaño: se supone un panel de 380×200 mm al 100 % (`ViewportWidthMm/HeightMm`) y se retrocede media ventana para centrar el globo. Limitado a la hoja (globo en el borde → visible pero no centrado).
- Hoja (m, origen abajo-izq.) → PDF: `MediaBox.X1 + x·(anchoPág/anchoHoja)`, sin invertir Y. El destino usa `page.Reference` (no el número de objeto) porque PdfSharp renumera al guardar.

El dibujo **no se modifica**: SolidWorks exporta el PDF (`SaveAs3` + `ExportPdfData`) y PdfSharp 1.50.5147 lo post-procesa.

Prototipo: `src/Veradel.SolidworksConsole/BalloonLocation/` → después pasa al addin como comando.

## Decisiones

- **Líneas:** post-proceso con PdfSharp, no croquis temporal en SW.
- **Globo ↔ fila LDM:** por nº de posición (texto del globo = columna nº de elemento). Globo LDM → `GetBomBalloonText(true)`, si no `GetText()`.
- **Varias LDM:** se pregunta al usuario cuál usar.

## Cálculo de zonas

- Tamaño de hoja: `Sheet.GetSize(ref w, ref h)` (m).
- Marco de zonas: hoja menos `Sheet.GetZoneMargin(swZoneMargin_e)`.
- Límites: barrido cada 2 mm con `Sheet.GetDrawingZone(x, y)` por la línea media del marco (en horizontal solo cambia la columna, en vertical la fila) + bisección hasta 0.01 mm. Vale para `EvenlySized` y `50mmFromCenter` sin conocer la fórmula.
- Etiqueta de cada celda: `GetDrawingZone` en su centro.

## Globos

- Se recorren todas las vistas de cada hoja con `GetFirstView()`/`GetNextView()` (la primera es la propia hoja → incluye globos sueltos en la hoja).
- Posición: centro del círculo = `Note.GetBalloonInfo()[0..1]` (coordenadas de hoja, m). Verificado en plano real (1732.160.SUB200), también en pilas. `Annotation.GetPosition()` es el punto del texto (~1 mm desplazado): un globo junto a un límite caía en la zona vecina → solo como respaldo.
- Pilas: además del propio globo, el maestro aporta `GetBalloonStack().Stack`; los duplicados no importan porque se agrupa por `HOJA-ZONA`.

## LDM

- Se lee celda a celda con `DisplayedText2[r, c, false]` (solo visible).
- Cabecera abajo (`swTableHeader_Bottom`) → se invierten las filas para que la pos. 1 salga arriba.
- Columna de posición: `GetColumnType2 == swBomTableColumnType_ItemNumber`, si no por título (POS/ELEM/ITEM/Nº).

## Pendiente

- Probar en Acrobat, Edge/Chrome y Firefox; ajustar `FocusZoom` / tamaño de ventana supuesto si hace falta.
- Pasar a comando del addin con diálogo para elegir LDM.
