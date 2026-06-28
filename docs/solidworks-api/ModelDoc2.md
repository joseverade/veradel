# Interfaz `IModelDoc2`

## Resumen

Permite interactuar con un **documento abierto** de SolidWorks. Son las propiedades y métodos
**comunes** a los tres tipos de documento:

- [`PartDoc`](PartDoc.md) — pieza
- [`AssemblyDoc`](AssemblyDoc.md) — ensamblaje
- [`DrawingDoc`](DrawingDoc.md) — plano

Un `ModelDoc2` representa el documento; para lo específico de cada tipo se hace *cast* al interfaz
correspondiente (`model as PartDoc`, etc.). Las unidades de la API son **metros** y **radianes**.

## Accesors

Cómo se obtiene un `IModelDoc2` en el add-in:

```csharp
// Documento activo (el que el usuario tiene delante):
IModelDoc2 model = _sw.IActiveDoc2 as IModelDoc2;   // _sw es ISldWorks

// Funcionalidad ampliada del mismo documento:
ModelDocExtension ext = model.Extension;            // ver ModelDocExtension.md
```

## Casos de uso

### Acotar un diámetro

Para que SolidWorks interprete la cota como **Ø** (diámetro) y no como radio: seleccionar la línea
del perfil **y** la línea constructiva (eje de revolución), y colocar el texto de la cota en el punto
**simétrico al otro lado del eje** (Y negativo, espejo de la arista).

```csharp
model.ClearSelection2(true);
line.Select4(false, null);       // la generatriz del perfil (a y = +r)
axis.Select4(true, null);        // la línea constructiva sobre el eje X (y = 0)
model.AddDiameterDimension2(x, -r, 0);   // texto al lado opuesto (y = -r) => Ø
```

### Crear todas las relaciones de un croquis exacto

Con `SketchManager.AddToDB = true` la geometría entra con coordenadas exactas (sin *snapping* a la
rejilla). Después, las relaciones se infieren de golpe con `ISketch.ConstrainAll` (ver
[Sketch](Sketch.md)). `ConstrainAll` **solo** relaciona entidades dentro del croquis; las
coincidencias con el origen o con caras del modelo se añaden a mano con `SketchAddConstraints`
tras seleccionar las dos entidades.

## Propiedades

| Tipo | Propiedad | Descripción |
|------|-----------|-------------|
| [`ModelDocExtension`](ModelDocExtension.md) | `Extension` | Funcionalidad ampliada del documento (selección por id, chaflanes, guardado avanzado…). |
| [`SketchManager`](SketchManager.md) | `SketchManager` | Gestor de croquis: abrir/cerrar croquis y crear entidades. |
| [`FeatureManager`](FeatureManager.md) | `FeatureManager` | Gestor del árbol: crea operaciones (revolución, chaflán…). |
| `SelectionMgr` | `SelectionManager` | Acceso a lo que hay seleccionado actualmente. |
| `ModelView` | `ActiveView` / `IActiveView` | Vista gráfica activa (zoom, orientación…). |

## Métodos

### Selección y vista

* `void ClearSelection2(bool IncludeSketchObjects)`
  * `IncludeSketchObjects` — `true` para deseleccionar también objetos de croquis.

* `void ViewZoomtofit2()` — encuadra todo el modelo en la vista.

* `void ViewZoomToSelection()` — hace zoom a lo seleccionado.

* `void GraphicsRedraw2()` — fuerza el redibujado de la vista.

### Información del documento

* `int GetType()` — tipo de documento; comparar con `swDocumentTypes_e` (`swDocPART`=1,
  `swDocASSEMBLY`=2, `swDocDRAWING`=3).

* `string GetTitle()` — título de la ventana del documento.

* `string GetPathName()` — ruta completa en disco (cadena vacía si no se ha guardado).

* `int GetFeatureManagerWidth()` — ancho actual del panel del árbol (px). *(oficial pendiente)*

### Árbol de operaciones

* [`Feature`](Feature.md) `FirstFeature()` — primera operación del árbol; se recorre con
  `Feature.GetNextFeature()`. Se usa para localizar planos/origen por **tipo** (independiente del
  idioma), p. ej. `GetTypeName2() == "RefPlane"` o `"OriginProfileFeature"`.

### Croquis: cotas y relaciones

* [`DisplayDimension`](DisplayDimension.md) `AddDiameterDimension2(double X, double Y, double Z)`
  — añade una cota de **diámetro** a las entidades seleccionadas (línea + eje constructivo).
  * `X` — posición X del texto de la cota (metros).
  * `Y` — posición Y del texto; debe quedar al **lado opuesto** del eje (espejo) para que sea Ø.
  * `Z` — posición Z (0 en un croquis 2D).
  * *Devuelve* el `DisplayDimension` creado (la sobrecarga COM tipa el retorno como `object`).

* [`DisplayDimension`](DisplayDimension.md) `AddHorizontalDimension2(double X, double Y, double Z)`
  — añade una cota **horizontal** (distancia en X) a lo seleccionado.
  * `X`, `Y`, `Z` — posición del texto de la cota (metros).

* `int SketchAddConstraints(string ConstraintType)` — añade una relación geométrica a la(s)
  entidad(es) **ya seleccionada(s)**. Identificadores usados en el add-in:
  * `"sgHORIZONTAL2D"` — horizontal.
  * `"sgVERTICAL2D"` — vertical.
  * `"sgCOINCIDENT"` — coincidente (punto-punto, punto-arista o punto-silueta). Para anclar un
    punto de croquis al **origen** se selecciona antes `EXTSKETCHPOINT` en `(0,0,0)`; a una
    **arista** del cuerpo se usa `EDGE`; a una **cara cilíndrica** vista de canto, `SILHOUETTE`
    (ver [ModelDocExtension](ModelDocExtension.md) `SelectByID2`).
  * *(Se prefiere a `ISketchRelationManager.AddRelation`, que lanza un AccessViolation no capturable.)*

### Reconstrucción

* `bool EditRebuild3()` — reconstruye el modelo (equivale a Ctrl+B). Devuelve `true` si reconstruye
  sin errores. *(oficial pendiente)*

### Anotaciones / guardado (otros comandos)

* `Note InsertNote(string Text)` — inserta una nota en la vista activa. *(oficial pendiente)*

* `bool Save3(int Options, ref int Errors, ref int Warnings)` — guarda el documento.
  * `Options` — banderas `swSaveAsOptions_e`.
  * `Errors` / `Warnings` — salida con códigos `swFileSaveError_e` / `swFileSaveWarning_e`.
  * *(oficial pendiente)*

---

### Métodos relacionados en otros interfaces

Estos se llaman sobre objetos obtenidos desde `ModelDoc2` y se documentan en su propio archivo:

- `Extension.SelectByID2(...)` → [ModelDocExtension](ModelDocExtension.md)
- `SketchManager.InsertSketch / CreateLine / CreateCenterLine / AddToDB / ActiveSketch` → [SketchManager](SketchManager.md)
- `Sketch.ConstrainAll()` → [Sketch](Sketch.md)
- `FeatureManager.FeatureRevolve2 / InsertFeatureChamfer` → [FeatureManager](FeatureManager.md)
- `Feature.Select2 / GetTypeName2 / GetNextFeature` → [Feature](Feature.md)
