# Despiece de calderería — orientación de las vistas relativas

Objetivo: en cada vista del despiece, la **cara de mayor área mira al usuario** y la pieza queda
**en ángulo recto** con la hoja (nada de piezas inclinadas).

Implementado en `src/Veradel.SolidworksConsole/Boilmaker/Boilermaker.cs` (aplicación de consola de pruebas).

## El problema

`IDrawingDoc::CreateRelativeView` necesita **dos** caras: una marca 1 (dirección FRONT) y una marca 2
(dirección RIGHT). La marca 1 resuelve "qué cara mira al usuario", pero la marca 2 decide el giro de
la pieza dentro del plano de la hoja. El código anterior usaba `Face2::GetNextFace()` para la segunda
cara, y eso no devuelve la cara contigua: recorre la lista enlazada de **todas** las caras del cuerpo.
La cara que salía era arbitraria y la vista aparecía girada un ángulo cualquiera.

## Solución en dos mecanismos

1. **Elección de la cara secundaria**: se busca entre las caras perpendiculares a la principal la que
   más apunta hacia la dirección dominante de sus aristas. Así el eje derecho de la vista nace ya casi
   alineado con la esquina recta.
2. **Corrección del giro residual**: después de crear la vista se mide el ángulo real de la arista
   dominante proyectada en la hoja y se corrige con `IView::Angle`. Esto es lo que **garantiza** el
   ángulo recto, aunque la cara secundaria no fuera perfecta.

Al separar las dos cosas, la marca 2 deja de ser crítica: basta con que sea perpendicular.

## Pasos del algoritmo

### 1. Cara principal — `GetLargestPlanarFace`

Mayor área entre las caras **planas** (`Surface::Identity() == PLANE_TYPE`).

Sin filtro de cáscara: `Face2::GetShellType()` no distingue exterior/interior como dice la documentación
y descartaba todas las caras. No hace falta, la cara de mayor área siempre es exterior. Si aun así no
aparece ninguna cara plana, `BoilermakerDebug.PrintFaceTypes` vuelca `identity` y `shellType` de cada cara.

En una chapa hay dos caras con la misma área (frente y dorso). El desempate usa el producto escalar
de la normal contra una dirección fija sin simetría con los ejes `(1,2,3)/√14`: siempre gana la misma
cara, así la vista no sale espejada de una ejecución a otra.

### 2. Marco local de la cara — `GetDominantEdgeDirection`

Sobre las aristas de la cara principal:

- se quedan solo las de tipo `LINE_TYPE`;
- dirección `d = fin − inicio`, normalizada, y proyectada al plano de la cara: `d −= (d·n)·n`;
- dos aristas del mismo eje con sentido opuesto son la misma dirección (`|d1·d2| > 1 − tol`): se acumulan
  sus longitudes;
- **dirección dominante `u`** = la de mayor longitud acumulada (en una chapa rectangular, el lado largo);
- **esquina recta** = existe otra dirección `v` con `|u·v| < tol`. Se informa por consola; si no hay
  ninguna (cara circular), no se corrige el giro.

### 3. Cara secundaria — `GetPerpendicularFace` / `PickPerpendicular`

Las caras vecinas reales salen de `IEdge::GetTwoAdjacentFaces2()` sobre las aristas de la cara principal.
Filtro: plana y `|n_A · n_B| < tol` (perpendicular). Entre las que pasan, se elige la de mayor
`n_B · u`, es decir, la que apunta más hacia la dirección dominante.

Si ninguna vecina sirve (la cara solo toca redondeos o cilindros), se repite la búsqueda sobre todas las
caras del cuerpo.

### 4. Creación de la vista

Marca 1 = cara principal → `FRONT` (0). Marca 2 = cara perpendicular → `RIGHT` (1).
`FRONT` hace que la normal de la cara principal apunte al observador: **la cara grande siempre al frente
por construcción**.

Las marcas se ponen con `SelectionMgr::AddSelectionListObject` sobre el documento de la **pieza**, y la
llamada `CreateRelativeView` se hace sobre el **dibujo**.

### 5. Corrección del giro — `AlignViewToRightAngle`

```csharp
MathTransform modelToView = view.ModelToViewTransform;
MathVector inView = (MathVector)math.CreateVector(u).MultiplyTransform(modelToView);
double[] d = (double[])inView.ArrayData;
double angle = Math.Atan2(d[1], d[0]);
view.Angle = view.Angle + AngleSign * angle;
```

Dos modos, con el interruptor `AlignDominantEdgeHorizontal`:

| Valor | Resultado |
|---|---|
| `true` (por defecto) | la arista dominante queda horizontal → pieza apaisada |
| `false` | se pliega el ángulo a `[−45°, 45°]` → giro mínimo hasta el ángulo recto más cercano |

`AngleSign` está a `-1.0`. Si el giro sale al revés (el error se duplica en vez de anularse), se cambia
a `1.0`; el signo depende del convenio de `IView::Angle`.

### 6. Perfil y planta — `CreateProjectedViews`

Cada cuerpo lleva **tres vistas**: la relativa hace de **alzado**, y de ella cuelgan **perfil** y
**planta** como vistas proyectadas (`IDrawingDoc::CreateUnfoldedViewAt3`). Al ser hijas heredan giro y
escala del alzado y quedan alineadas con él; no hay que repetir el cálculo de caras.

Precondición de la llamada: la vista padre debe estar **activa y seleccionada** →
`ActivateView(name)` + `SelectByID2(name, "DRAWINGVIEW", …)`. Después de crear una hija la activa pasa a
ser ella, así que hay que volver a seleccionar el padre para la segunda.

`CreateUnfoldedViewAt3` deduce **qué** vista es por la zona en que se suelta respecto al recuadro del
padre, y ese lado depende del convenio de proyección de la hoja:

| Diedro | Perfil | Planta |
|---|---|---|
| Primero (europeo, ISO) | a la izquierda | debajo |
| Tercero (americano) | a la derecha | encima |

El convenio se lee de la hoja, no se fija en código: `ISheet::GetProperties2()`, **índice 4**
(`true` = primer diedro).

**Excepción pedida: el perfil va siempre a la derecha** (`ProfileOnRight = true`). En una hoja de primer
diedro eso significa que la proyección que sale es la **vista por la izquierda**, no la de la derecha —
la zona de suelta es la que manda. Se avisa por consola al arrancar. Poniendo `ProfileOnRight = false`
manda otra vez el diedro de la hoja.

Cada hija tiene su propio signo, `ProfileSign` y `PlanSign` (±1), y esos dos valores gobiernan tanto la
creación como el reparto en la hoja; no queda ningún `_firstAngleProjection` suelto en el cálculo de
posiciones.

**El punto de suelta se mide desde el centro del outline del padre, no desde `IView::Position`.**
`Position` es el origen del modelo proyectado en la hoja, y en un despiece cada cuerpo cae lejos del
origen de la pieza: al sumar el desplazamiento sobre `Position` el punto caía en una **zona diagonal**
del recuadro y SolidWorks creaba una **axonométrica** en lugar del perfil o la planta. Solo pasaba en
algunos cuerpos — justo los alejados del origen.

La distancia del primer emplazamiento da igual, solo cuenta la dirección. La posición definitiva se fija
en `PlaceViewGroup`, ya con el tamaño real de cada vista.

#### Alineación por centro

Por defecto la hija queda atada al padre **por origen de vista**. Se cambia a centro con
`IView::AlignWithView(alignType, baseView)` y `swAlignViewTypes_e`:

| Vista | Alineación |
|---|---|
| Perfil | `swAlignViewHorizontalCenter` (2) |
| Planta | `swAlignViewVerticalCenter` (3) |

#### Descarte de axonométricas — `IsOrthogonalProjection`

Verificación sin depender de nombres de orientación: en una proyección ortogonal los ejes de la vista
hija son los del padre **permutados y con signo**, así que `C · Pᵗ` (matrices de giro de
`ModelToViewTransform`) solo contiene `0` y `±1`. En una isométrica salen `0.408`, `0.577`, `0.707`.

Si la vista no pasa el test se borra (`DeleteSelection2`) y se reintenta midiendo desde `Position`; si
tampoco, se informa `sin perfil` / `sin planta` y el despiece continúa.

El filtro de cuerpo (`IView::Bodies`) se aplica a las tres, no solo al alzado: `ApplyBodyFilter` va dentro
de `try/catch` porque en una vista hija puede fallar sin que eso invalide el resto del despiece.

### 7. Hoja propia del despiece — `CreateDespieceSheet`

El despiece **no** se inserta en la hoja de partida: se crea una hoja nueva llamada
`despiece - veradel` y todas las vistas caen ahí. La hoja original queda intacta.

`IDrawingDoc::NewSheet3(Name, PaperSize, TemplateIn, Scale1, Scale2, FirstAngle, TemplateName, Width, Height, PropertyViewName)`
clona las propiedades de la hoja de partida:

| Dato | De dónde sale |
|---|---|
| Papel, plantilla, escala1, escala2, diedro | `ISheet::GetProperties2()` índices 0–4 (mismo orden que `SetProperties`) |
| Ancho y alto | `ISheet::GetSize()` — en papel a medida no vienen en las propiedades |
| Formato de hoja | `ISheet::GetTemplateName()` |

El nombre de hoja debe ser único dentro del dibujo: `UniqueSheetName` compara contra
`GetSheetNames()` (sin distinguir mayúsculas) y numera `despiece - veradel 2`, `3`… si ya existe.

`NewSheet3` deja la hoja activa, pero se llama igualmente a `ActivateSheet(name)`: tanto
`CreateRelativeView` como `CreateUnfoldedViewAt3` crean **siempre en la hoja activa**, así que si la
activa no fuera la nueva el despiece iría a parar a la de partida.

La hoja se crea **después** de volver al dibujo con `ActivateDoc3` (abrir la pieza cambia el documento
activo), y el ancho/alto que usa el reparto se releen de la hoja nueva.

### 8. Rejilla, escala y paginación

El reparto es una **rejilla fija** `GridColumns × GridRows` (3×2), no un empaquetado libre. Con escalas
distintas por pieza el flujo libre deja filas dentadas y rompe la correspondencia con la lista de cortes;
la rejilla da celda de tamaño fijo, orden respetado y reparto determinista: el cuerpo `n` cae siempre en
la hoja `n / (cols·filas)` y en la celda `n % (cols·filas)`.

#### Área útil

Márgenes constantes; el inferior reserva la banda del cajetín.

| Constante | Valor | Para qué |
|---|---|---|
| `MarginLeft` / `MarginRight` / `MarginTop` | 0.015 m | borde de la hoja |
| `MarginBottom` | 0.060 m | banda del cajetín |
| `CellPadding` | 0.005 m | aire entre bloque y borde de celda |

`cellW = (sheetW − MarginLeft − MarginRight) / GridColumns`, ídem en alto. El área donde se mete el
bloque es la celda menos `2·CellPadding` por lado.

#### Tres fases

La escala no se puede decidir antes de crear las vistas: el tamaño del bloque solo se conoce con las
vistas hechas. De ahí el orden:

1. **Crear y medir** — se recorren las hojas; en cada una se crean hasta `cols·filas` bloques y se mide
   cada uno a 1:1. Los cuerpos sin par de caras válido se descartan **antes** de paginar (`printable`),
   si no dejarían huecos en la rejilla.
2. **Escalar** — una sola pasada sobre todo el despiece, porque el modo uniforme necesita conocer la
   pieza más exigente antes de fijar nada.
3. **Colocar** — `PlaceInCell` con el tamaño definitivo de cada vista.

#### Medida a 1:1 — `MeasureBlock`

`GetOutline` devuelve el contorno **ya escalado**, en metros de papel. El tamaño real sale de dividir por
`IView::ScaleDecimal`. (`IBody2::GetBodyBox` no sirve: da la caja en ejes del modelo, no en los de la
vista.)

#### Elección de la escala — `FitScale` + `SnapDown`

```
needW = frontW1:1 + profileW1:1        freeW = innerW − (hay perfil ? ViewGap : 0)
needH = frontH1:1 + planH1:1           freeH = innerH − (hay planta ? ViewGap : 0)
fit   = min(freeW / needW, freeH / needH)
```

Los huecos entre vistas van en unidades de papel y **no** escalan con la pieza, por eso se restan antes
de dividir. El valor en crudo (1:6.37) no se lee ni se acota, así que `SnapDown` baja al escalón
normalizado inmediatamente inferior de la serie ISO 5455:

`50:1 · 20:1 · 10:1 · 5:1 · 2:1 · 1:1 · 1:2 · 1:5 · 1:10 · 1:20 · 1:50 · 1:100 · 1:200 · 1:500 · 1:1000`

Siempre **hacia abajo**, para que la pieza quepa de verdad. Si ni la menor de la serie basta, se avisa
por consola y se usa esa.

#### Modos de escala — `DespieceScaleMode`

| Modo | Qué hace |
|---|---|
| `PerBody` (por defecto) | cada cuerpo, la mayor escala normalizada que cabe en su celda |
| `Uniform` | una sola escala para todo el despiece: la que exige la pieza más grande |

Lo elige quien llama (`new Boilermaker(swApp, modo)` o la propiedad `ScaleMode`). En la consola de
pruebas, el argumento `uniforme` selecciona el segundo.

`PerBody` aprovecha la hoja; `Uniform` deja comparar tamaños a ojo a costa de que las piezas pequeñas se
queden diminutas.

La escala se fija **solo en el alzado**: `IView::UseSheetScale = 0` (ojo, es `int`, no `bool`) y
`IView::ScaleRatio = new double[] { num, den }`. Perfil y planta llevan `UseParentScale = true` y la
heredan. Efecto lateral útil: al no usar la escala de hoja, SolidWorks rotula solo `ESCALA 1:5` bajo
cada vista.

#### Colocación — `PlaceInCell`

El alzado **no** va al centro de la celda: hay que descontar el hueco que ocupan perfil y planta a su
lado, que es lo que centra el conjunto.

```
frontCenterX = cellCenterX − (rightPad − leftPad) / 2
frontCenterY = cellCenterY − (topPad − bottomPad) / 2
```

`leftPad`/`rightPad` y `topPad`/`bottomPad` salen de `ProfileSign` y `PlanSign`, así que el centrado
sigue el diedro sin condicionales sueltos. Fila 0 arriba: `cellCenterY = sheetH − MarginTop − (fila+0.5)·cellH`.

Primero se mueve el alzado; las hijas lo siguen por la alineación, pero se les fija la posición
explícitamente después de un `GraphicsRedraw2()`: sobre el eje alineado manda SolidWorks y sobre el eje
libre manda el valor que se pasa, así que la llamada es idempotente.

`IView::Position` es el centro de la vista, así que el desplazamiento se calcula contra el centro del
outline actual (`MoveViewCenterTo`). Si aun así el bloque se sale de su celda, se avisa por consola.

#### Paginación

Al llenarse la rejilla se crea otra hoja con `CreateDespieceSheet`, y `UniqueSheetName` la numera:
`despiece - veradel 2`, `3`… Tope de seguridad `MaxSheets = 50`; por encima se avisa y se recorta.

Sobre el caso de "500 piezas": `GetBodiesCutList` coge `oBodies[0]` de cada `CutListFolder`, o sea **un
representante por ítem de lista de cortes**, no por cuerpo. 500 cuerpos en una estructura suelen ser
15-30 ítems distintos. Falta añadir la **cantidad** de cada carpeta al rótulo.

## Correcciones de álgebra lineal

- `CrossProduct`: la componente Y tenía el signo cambiado. Correcto: `v1[2]*v2[0] − v1[0]*v2[2]`.
- `DotProduct`: el guard comprobaba `v1.Length` dos veces en vez de `v1` y `v2`.
- `GetOrientationVector` (ahora `GetLargestPlanarFace`): la semilla era `oFaces[0]` sin filtrar, así que una
  cara cilíndrica podía ganar sin competir.
- `SuspendSelectionList()` / `ResumeSelectionList2()` alrededor de las marcas: `Resume` restaura la lista
  anterior. Sustituido por `ClearSelection2` sobre la pieza.

## API verificada por reflexión

`IView::Angle` (double, get/set) · `IView::ModelToViewTransform` (MathTransform) · `IView::Position` ·
`IView::GetOutline` · `IView::GetName2` · `IView::Bodies` (get/set object) · `IFace2::IsSame(object)` ·
`IFace2::GetShellType` · `IEdge::GetTwoAdjacentFaces2` · `ICurve::LineParams` ·
`IMathVector::MultiplyTransform` · `swRelativeViewCreationDirection_e`
(FRONT=0, RIGHT=1, TOP=2, BACK=3, LEFT=4, BOTTOM=5, AUXILIARY=6) ·
`IDrawingDoc::CreateUnfoldedViewAt3(double X, double Y, double Z, bool NotAligned)` → `View`
(la v2 devuelve `bool`, no sirve) · `IDrawingDoc::ActivateView(string)` · `ISheet::GetProperties2()` ·
`IView::AlignWithView(int AlignType, View BaseView)` → `bool` · `IMathTransform::ArrayData`
(9 primeros valores = matriz de giro) · `swAlignViewTypes_e` (NoViewAlignment=0, Default=1,
HorizontalCenter=2, VerticalCenter=3, HorizontalOrigin=4, VerticalOrigin=5) ·
`swDeleteSelectionOptions_e` (Children=1, Absorbed=2, Advanced=4) ·
`IDrawingDoc::NewSheet3(string, int, int, double, double, bool, string, double, double, string)` → `bool` ·
`IDrawingDoc::ActivateSheet(string)` → `bool` · `IDrawingDoc::GetSheetNames()` → `object` ·
`ISheet::GetTemplateName()` · `ISheet::SetProperties(int PaperSize, int Template, double Scale1,
double Scale2, bool FirstAngle, double Width, double Height)` (fija el orden de `GetProperties2`) ·
`swDwgTemplates_e` (A4=6, A3=8, A2=9, A1=10, A0=11, Custom=12, None=13) ·
`swDwgPaperSizes_e` (A4size=6, A3size=8, UserDefined=12) ·
`IView::UseSheetScale` (**int**, get/set) · `IView::ScaleRatio` (`object`, `double[2]`) ·
`IView::ScaleDecimal` (double) · `IView::UseParentScale` (**bool**, get/set) ·
`IBody2::GetBodyBox()` (caja en ejes del modelo: no sirve para medir el bloque de vistas).

Verificadas pero **no usadas** hoy, por si vuelve el ajuste automático de papel:
`IBody2::GetExtremePoint(double X, double Y, double Z, out double Outx, out double Outy, out double Outz)` → `bool`
(extent exacto en cualquier dirección, también en cilindros) ·
`ISheet::SetSize(int Size, double Width, double Height)` → `bool` ·
`ISheet::SetProperties2(int PaperSz, int Templ, double Scale1, double Scale2, bool FirstAngle,
double Width, double Height, bool SameCustomPropAsSheetInDocProp)` ·
`ISheet::ReloadTemplate(bool KeepNoteChanges)` → `int` ·
`swDwgTemplates_e`/`swDwgPaperSizes_e` comparten valor para el mismo tamaño (A4=6, A3=8, A2=9, A1=10, A0=11).

Ojo: `IView::AlignDrawingView` usa otro enum, `swAlignDrawingViewTypes_e` (giro respecto a la hoja);
no es el de la alineación padre-hija.

Ojo: `GetSurface()`, `GetCurve()`, `GetViews()`, `GetPoint()` devuelven `System.Object` — hay que castear.

## Pendiente

- Cantidad de cada ítem de la lista de cortes en el rótulo de la vista.
- Rejilla y márgenes son constantes pensadas para A3: revisar en A4 y A2.
- Ajuste automático de papel A4–A0 según lo que mida el despiece: se implementó y se retiró.
  La API está verificada (ver arriba) y el criterio que funcionaba era escala mínima admisible
  (1:10) + rejilla adaptativa; el fallback tiene que ser "menor número de hojas que da la mayor
  escala alcanzable", nunca `MaxSheets`.
- Elección del modo de escala desde la interfaz del add-in, no solo por argumento de consola.
- Portar a `SolidWorksService.Boilermaking.cs` cuando las pruebas de consola cuadren.
