# Espejar pieza manteniendo el árbol de operaciones

> Nota de diseño guardada para el futuro. No implementado todavía.
> Datos de API verificados por reflexión sobre `SolidWorks.Interop.sldworks.dll` (SW 2026).

## Contexto

Hoy el add-in no tiene nada de espejo geométrico: la única llamada relacionada en todo el repo
es un `SketchMirror()` en el proyecto de consola de referencia
(`src/Veradel.SolidworksConsole/PartCreation/ChompCreator.cs:814`), y `IsMirroredFeature` /
`IComponent2.IsMirrored()` se usan solo para **detectar y rechazar** instancias espejadas.

Lo que SolidWorks ofrece de fábrica (`IPartDoc.MirrorPart2`, `IFeatureManager.InsertMirrorFeature2`)
produce geometría correcta pero un árbol de **un solo nodo derivado**: no editable, no paramétrico.
El objetivo es generar la pieza simétrica **con el árbol reconstruido operación por operación**,
sobre piezas arbitrarias del usuario, abortando con un listado cuando algo no se pueda replicar.

Viabilidad: `IFeature.GetDefinition()` cubre **173 interfaces `*FeatureData`**, los croquis son
legibles íntegros (`GetSketchSegments`, `ModelToSketchTransform`, `RelationManager`) y la resolución
de referencias topológicas se puede hacer por coordenada espejada — exactamente el mismo mecanismo
(`SelectByID2("", "FACE"/"EDGE", x, y, z, …)`) sobre el que ya se apoya todo `ShaftBuilder.cs`.

### Decisiones tomadas

| Pregunta | Decisión |
|---|---|
| Alcance | Cualquier pieza del usuario, no solo las del configurador |
| Operaciones | Todas las que la API permita leer y reescribir |
| Fallo | Abortar y avisar con el listado de operaciones culpables |
| Plano de simetría | Los 3 planos estándar (Front / Top / Right) |

---

## Enfoque

Motor de **replay**: leer el árbol original en orden, y por cada operación construir la equivalente
espejada en un documento nuevo. Nada de derivadas. Dos pasadas:

1. **Auditoría (dry run)** — recorre el árbol, clasifica cada operación con el registro de handlers
   y devuelve un `MirrorPlan` COM-free. Si hay **bloqueantes**, se aborta y se listan.
2. **Construcción** — solo si la auditoría pasa. Replay en orden en el documento nuevo.

Regla de aborto: cualquier operación sin handler, o cuyo `GetDefinition()` devuelva `null`
(macro features, library features, cuerpos importados), o con referencias externas / in-context /
ecuaciones / tablas de diseño → **bloqueante**.
Las pérdidas menores (una relación de croquis sin equivalente `sgXXX`, una cota conducida) son
**degradaciones**: se muestran en el diálogo de confirmación, no abortan.

### El núcleo matemático

Espejo respecto a plano estándar ⇒ negar una coordenada. `M = I − 2nnᵀ`, `det(M) = −1`.

Por cada croquis: tomar `ISketch.ModelToSketchTransform`, `.Inverse()` → marco croquis→modelo
(`MathTransform.ArrayData`: `[0..8]` rotación fila-mayor, `[9..11]` traslación, `[12]` escala).
Sus ejes son `X`, `Y`, `Z` y origen `O`. El marco espejado directo (`M·X`, `M·Y`, `M·Z`) es
**levógiro** y SolidWorks lo rechaza. Descomposición usada:

```
Xn = −M·X      Yn = M·Y      Zn = Xn × Yn      On = M·O
puntos 2D:  (u, v) → (−u, v)
arcos:      intercambiar start/end; GetRotationDir cambia de signo
```

**Invariante que sale de esta elección:** `Zn` queda siempre negada respecto a `M·Z`, es decir, la
normal del croquis nuevo apunta al revés. Por tanto **todos los flags de dirección se invierten
siempre** (`ReverseDirection`, `FlipSideToCut`, `Ddir1/Ddir2`, `OffsetReverse1/2`, `RevDir` del
taladro, flip del chaflán). Es una regla constante, no caso por caso — **verificar empíricamente en
la fase 2 antes de replicarla al resto de handlers.**

### Resolución de referencias topológicas

`GetPersistReference3` es **por documento**, no viaja al documento nuevo. Sustituto:

```
punto sobre la cara original  (Face2.GetClosestPointOn desde el centro de GetBox)
      → espejar el punto
      → newModel.Extension.SelectByID2("", "FACE", mx, my, mz, …)
      → validar: normal ≈ normal espejada  Y  GetArea ≈ área original
      → si no valida: bloqueante en tiempo de construcción, abortar y borrar el doc nuevo
```

Funciona porque el replay va **en orden**: cuando toca un redondeo, el cuerpo previo ya existe.
Para aristas, punto medio vía `Edge.GetCurve().Evaluate2`. Es el mismo patrón que
`CoincidentPointToEdge` en `SolidWorksService.PartBuilderCommon.cs:169`.

**Gotcha obligatorio:** los getters de `*FeatureData` que devuelven referencias
(`GetEndConditionReference`, `GetFace`, `GetFromEntity`, `GetVertex`) exigen
`AccessSelections(topDoc, null)` antes y `ReleaseSelectionAccess()` después, siempre en `finally`.

---

## Arquitectura (sigue las convenciones de PIPELINE.md §5)

| Archivo | Qué es |
|---|---|
| `src/VeradeAddin/Models/MirrorPlan.cs` | DTO de auditoría: `IsPart`, `PartPath`, `List<MirrorFeatureInfo>` (`Name`, `TypeName`, `Kind` = Soportada/Degradada/Bloqueante, `Reason`), `bool CanMirror` |
| `src/VeradeAddin/Models/MirrorRequest.cs` | `StandardPlane Plane` (Front/Top/Right), `bool AcceptDegradations` |
| `src/VeradeAddin/Models/MirrorResult.cs` | `FeaturesReplayed`, `Skipped`, `NewDocTitle`, `Error`, `Warning` |
| `src/VeradeAddin/Services/MirrorMath.cs` | **COM-free**, estático: `PlaneAxis(StandardPlane)`, `MirrorPoint`, `MirrorFrame(double[16], int axis)`, `MirrorSketchPoint`. El repo no tiene capa de matemáticas; esta es mínima y testeable aislada. |
| `src/VeradeAddin/Services/SolidWorksService.Mirror.cs` | Partial nuevo. Todo el COM. Métodos públicos: `MirrorPlan InspectActivePartForMirror()`, `MirrorResult MirrorActivePart(MirrorRequest)` |
| `src/VeradeAddin/Services/Mirror/IMirrorFeatureHandler.cs` + handlers | Registro por `GetTypeName2()` |
| `src/VeradeAddin/Commands/EspejarPiezaCommand.cs` | `ICommand`, `DocumentTypes = { Part }`, `CanExecute` = pieza activa con cuerpos |
| `src/VeradeAddin/UI/MirrorPlanDialog.cs` + método en `IDialogService`/`WinFormsDialogService` | Selector de plano + listado de bloqueantes/degradaciones |

Dos líneas más: `.Add(new EspejarPiezaCommand(...))` en `CompositionRoot`, y los
`<Compile Include>` en `VeradeAddin.csproj` (proyecto legacy, lista los archivos explícitamente).
Más un `CommandIcon` nuevo + su `case` en `IconStripFactory`.

### Registro de handlers

```csharp
interface IMirrorFeatureHandler
{
    string   TypeName { get; }                                  // GetTypeName2()
    bool     CanReplay(IFeature f, out string blockingReason);  // pasada de auditoría
    IFeature Replay(MirrorContext ctx, IFeature original);      // pasada de construcción
}
```

`MirrorContext` lleva: doc origen, doc destino, eje de espejo, el resolvedor de caras/aristas y el
mapa `featureOriginal → featureNueva` (para patrones y planos de referencia que apuntan a otras
operaciones). Un `GetTypeName2()` sin handler registrado ⇒ bloqueante automático — esto es lo que
hace que el motor crezca por incrementos sin mentir nunca sobre lo que sabe hacer.

Handlers y su API de escritura (firmas verificadas por reflexión):

| `GetTypeName2()` | Lectura | Escritura |
|---|---|---|
| `ProfileFeature` | `GetSpecificFeature2() as ISketch` | `InsertSketch` + `CreateLine`/`CreateArc`/`CreateCircle`/`CreateSpline` |
| `RefPlane`, `RefAxis` | `IRefPlaneFeatureData` | `InsertRefPlane(...)`, `InsertAxis2` |
| `Extrusion`, `Boss`, `Cut`, `*Thin` | `IExtrudeFeatureData2` | `FeatureExtrusion3(Sd, Flip, Dir, T1, T2, D1, D2, Dchk1, Dchk2, Ddir1, Ddir2, Dang1, Dang2, OffsetReverse1, OffsetReverse2, TranslateSurface1, TranslateSurface2, Merge, UseFeatScope, UseAutoSelect, T0, StartOffset, FlipStartOffset)` |
| `Revolution`, `RevCut` | `IRevolveFeatureData2` | `FeatureRevolve2(SingleDir, IsSolid, IsThin, IsCut, ReverseDir, BothDirectionUpToSameEntity, Dir1Type, Dir2Type, Dir1Angle, Dir2Angle, …)` |
| `Fillet` | `ISimpleFilletFeatureData2` / `IVariableFilletFeatureData2` | `FeatureFillet3(Options, R1, R2, Rho, Ftyp, OverflowType, ConicRhoType, Radii, Dist2Arr, RhoArr, SetBackDistances, PointRadiusArray, …)` |
| `Chamfer` | `IChamferFeatureData2` | `InsertFeatureChamfer(Options, ChamferType, Width, Angle, OtherDist, VertexChamDist1..3)` |
| `HoleWzd` | `IWizardHoleFeatureData2` | `HoleWizard5(GenericHoleType, StandardIndex, FastenerTypeIndex, SSize, EndType, Diameter, Depth, Length, Value1..12, ThreadClass, RevDir, FeatureScope, AutoSelect, …)` |
| `CosmeticThread` | `ICosmeticThreadFeatureData` | `InsertCosmeticThread3` |
| `LPattern`, `CirPattern` | `ILocalLinearPatternFeatureData` etc. | `FeatureLinearPattern5(Num1, Spacing1, Num2, Spacing2, FlipDir1, FlipDir2, …)`, `FeatureCircularPattern5(...)` |

**Reutilizar, no reescribir:** `SolidWorksService.HoleInfo.cs` (1666 líneas) ya lee
`IWizardHoleFeatureData2`, `ICosmeticThreadFeatureData` y `IAdvancedHoleFeatureData` — el handler
de taladros parte de ahí. `SolidWorksService.PartBuilderCommon.cs` ya aporta `Line`,
`ConstrainAll`, `StartPt`/`EndPt`, `RestoreSketch`, `CoincidentPointToEdge`, `LengthDim`,
`DiameterDim`. `ShaftBuilder.cs` aporta `AddConstraintGuarded`, `TryDrivingDim`,
`SketchFullyDefined`, `ForceDrivingDims` (~líneas 1589-1682) — el replay de relaciones y cotas
debe usarlos, no duplicarlos.

### Relaciones y cotas de croquis

Leer con `ISketch.RelationManager.GetRelations()` → `ISketchRelation.GetRelationType()`
(`swConstraintType_e`, 86 valores) + `GetEntities()`. Reescribir seleccionando las entidades
equivalentes (mapeadas por índice de creación: los segmentos se crean en el mismo orden) y llamando
`IModelDoc2.SketchAddConstraints("sgXXX")`. **No usar `AddRelation`** — lanza un
`AccessViolation` no capturable.

Tabla de mapeo `swConstraintType_e → "sgXXX"` a mano. Invariantes al espejo: coincident, tangent,
equal, concentric, parallel, perpendicular, horizontal, vertical. **Se intercambian**:
`ARCANGLEFT`↔`ARCANGRIGHT`, `ELLIPSEANGLEFT`↔`ELLIPSEANGRIGHT`. Sin equivalente `sg`
(`PATTERNED`, `SNAP*`, `FAKESLOT*`, …) → **degradación**, no bloqueante.

Cotas: `IFeature.GetFirstDisplayDimension`/`GetNextDisplayDimension` → `GetDimension2(0)`.
Valores escalares, invariantes salvo angulares con signo.

### Documento de salida

`ISldWorks.NewDocument` con la plantilla de pieza por defecto
(`GetUserPreferenceStringValue(swDefaultTemplatePart)`), título `<original>_ESP`. **No se guarda
solo** — se deja abierto y activo para que el usuario decida. Si el replay aborta a mitad, se cierra
el documento nuevo sin guardar (`CloseDoc`) para no dejar basura.

Silencio durante el bulk: `ModelView.EnableGraphicsUpdate = false` + `ISldWorks.CommandInProgress = true`,
restaurados en `finally` — mismo patrón que `SolidWorksService.Boilermaking.cs`.

---

## Orden de trabajo

Cada fase es entregable y deja el comando en un estado honesto (lo no soportado aborta con listado).

1. **Auditoría sola, sin geometría.** `MirrorMath`, DTOs, el recorrido del árbol, el registro vacío
   de handlers, el comando y el diálogo. Resultado: el usuario ve exactamente qué se puede y qué no
   de su pieza. Des-arriesga todo lo demás y es útil por sí solo.
2. **Croquis + Extruir/Cortar.** Aquí se valida la invariante de inversión de flags y la
   descomposición del marco. Es el punto donde se confirma o se corrige el modelo mental.
3. **Revolución, `RefPlane`, `RefAxis`, `SplitLine`.**
4. **Redondeos y chaflanes** → construir aquí el resolvedor de caras/aristas por coordenada
   espejada (con validación normal+área).
5. **Hole Wizard + roscas cosméticas**, partiendo de `HoleInfo.cs`.
6. **Patrones** (lineal, circular, y `MirrorPattern`, que al espejar la pieza puede necesitar
   invertir su propio plano).

---

## Verificación

Compilar: `MSBuild src/VeradeAddin/VeradeAddin.csproj /p:Configuration=Debug`
(`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`),
registrar con `regasm /codebase` elevado, abrir SolidWorks.

Casos de prueba, en este orden:

1. **Pieza quiral generada por el propio add-in**: eje del configurador con chavetero a `AngleDeg = 30`
   y un `ShaftCenterHole` en un solo extremo. Espejar respecto a **Right** (el plano que rompe la
   simetría del chavetero). Comprobar: el árbol nuevo tiene el mismo número de operaciones, todas
   editables, y el chavetero sale a −30°.
2. **La misma pieza respecto a Front** (plano perpendicular al eje): invierte el orden de los
   escalones. Verifica que la inversión de flags de dirección es correcta en el caso opuesto.
3. **Pieza hecha a mano**: croquis en Front + extrusión + un par de redondeos + un taladro Hole
   Wizard descentrado. Es el caso que ejercita el resolvedor de caras.
4. **Caso de aborto**: pieza con un cuerpo importado (STEP) o una library feature. Debe abortar sin
   crear documento y listar la operación culpable por nombre.
5. **Contraste de geometría**: sobre cada caso que pase, hacer también `IPartDoc.MirrorPart2` a mano
   y comparar volumen y bounding box (`IBody2.GetBodyBox`, `IMassProperty`) entre la derivada y la
   replicada. Deben coincidir dentro de tolerancia — es el único chequeo objetivo de que el replay
   no mintió.

Los logs van a `%APPDATA%\VeradelAddin\logs\` vía `ILogger`; cada operación replicada o saltada se
registra con su `GetTypeName2()` para poder depurar un aborto sin adivinar.

---

## Riesgos conocidos

- **La invariante de inversión de flags puede no ser universal.** Está derivada de la
  descomposición elegida y es consistente en teoría, pero SolidWorks tiene convenciones propias por
  tipo de operación. La fase 2 existe para descubrirlo pronto y barato.
- **Splines**: `CreateSpline3` y los datos de control (`ISplineParamData`) son la parte más frágil
  del replay de croquis. Si dan problemas, marcarlos bloqueantes en v1 en lugar de producir
  geometría sutilmente distinta.
- **Chapa y weldments**: fuera de alcance, bloqueantes explícitos desde el principio.
- El proyecto usa `EmbedInteropTypes=True`; algunas interfaces `*FeatureData` poco comunes pueden
  requerir cast vía `dynamic` si el interop embebido no expone el miembro.

---

## Apéndice: alternativas descartadas

| Opción | Por qué se descartó |
|---|---|
| `IPartDoc.MirrorPart2(breakLink, options, out ModelDoc2)` | Una sola llamada, geometría siempre correcta, pero el árbol resultante es **un nodo derivado**: no editable ni paramétrico. Sirve solo como referencia de contraste en las pruebas. |
| `IFeatureManager.InsertMirrorFeature2(bMirrorBody, bGeometryPattern, bMerge, bKnit, scopeOptions)` | Espeja dentro de la **misma** pieza; no genera una pieza independiente. |
| Configuración izquierda/derecha en una sola pieza | Solo funciona si la geometría es trivialmente parametrizable; no generaliza. |
| Solo espejar piezas del configurador (transformar `ShaftSpec` y re-ejecutar `CreateShaft`) | 1-2 días y árbol perfecto, pero limitado a ejes/bulones registrados en `PartRegistry`. Descartado por alcance: se quiere cualquier pieza. |
