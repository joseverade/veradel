# Veradel Addin — Architecture & Pipeline

SolidWorks add-in (C#, .NET Framework 4.8, COM interop). Source of truth for how the
add-in is wired. **Keep it current as the code evolves.**

UI strings are in Spanish; code, logs and identifiers stay in English.

---

## 1. High-level flow

```
SolidWorks starts
   │
   ▼
COM loads VeradelAddin (registry keys written by regasm at install time)
   │
   ▼
ISwAddin.ConnectToSW(ThisSW, Cookie)              ── SwAddin.cs
   │   • cast ThisSW -> ISldWorks
   │   • SetAddinCallbackInfo2(0, this, cookie)    (so SW can call our callbacks)
   │   • build CompositionRoot                     ── Infrastructure/CompositionRoot.cs
   │   • CommandRegistrar.Register()               ── Infrastructure/CommandRegistrar.cs
   │
   ▼
CommandGroup created → one tab per document type via AddCommandTab
("Veradel Pieza" / "Veradel Assembly" / "Veradel Dibujo")
   │
   ▼
User clicks a button
   │
   ▼
SW invokes "RunCommand(i)" by name on VeradelAddin   ── SwAddin.cs
   │
   ▼
CompositionRoot.Execute(i)   ← centralised try/catch + error logging (safety net)
   │
   ▼
CommandRegistry[i].Execute()
   │
   ▼
Command calls services:
   ISolidWorksService  (active doc, selection hierarchy)   ── only place that touches COM
   IFileSystemService  (reveal in Explorer)
   IDialogService      (tree dialog / message boxes, Spanish)
   │
   ▼
ILogger.Log(...) -> ILogSink(s) -> JsonLinesFileSink (NDJSON file on disk)
   │
   ▼
ISwAddin.DisconnectFromSW()  -> CommandRegistrar.Unregister(), release COM
```

---

## 2. Layers & responsibilities

| Layer | Files | Responsibility |
|-------|-------|----------------|
| **Entry point** | `SwAddin.cs` | Implements `ISwAddin`. Connect/disconnect lifecycle, COM registration functions, and the two generic ribbon callbacks (`RunCommand`, `EnableCommand`). Pure dispatch. |
| **Composition root** | `Infrastructure/CompositionRoot.cs` | Manual DI. Builds the whole object graph via constructor injection. Single place to choose log sinks and register commands. **Also the command dispatcher**: `Execute(i)` wraps every command in a try/catch + error log + user message — new commands inherit this safety net for free. |
| **UI registration** | `Infrastructure/CommandRegistrar.cs`, `RibbonIds.cs` | Builds/tears down the ribbon from the command registry. Only class aware of SolidWorks `CommandManager`. Wires each button to `RunCommand(i)`/`EnableCommand(i)`. |
| **Commands** | `Commands/ICommand.cs`, `CommandRegistry.cs`, `OpenFolderCommand.cs` | Command pattern. Each command implements `ICommand`; registered once in the registry. Commands orchestrate services and log their per-branch outcomes (Success/Cancel); unhandled errors are caught by the dispatcher. |
| **Services** | `Services/*` | `ISolidWorksService` (COM isolation boundary — returns plain models), `IFileSystemService` (Explorer/shell, file copy/delete), `IScreenCaptureService` (GDI screen grab of the SW window), `IDialogService` (UI abstraction). |
| **Logging** | `Logging/*` | `ILogger` abstraction, `Logger` (timestamps + fan-out), `ILogSink` (swappable destination), `JsonLinesFileSink` (local NDJSON), `LogEntry`/`LogOutcome`. |
| **Models** | `Models/*` | COM-free DTOs: `ActiveDocument`, `ComponentNode`, `DocumentKind`, `CosmeticThreadResult`, `ComponentExtractionPlan`, `ExtractComponentResult`, `ScreenshotResult`, `ScreenshotAction`, `DrawingExportInfo`, `ExportRequest`, `ExportResult`/`ExportItem`, `EdgeColoringPlan`/`EdgeColorPartInfo`/`DetectedColor`, `EdgeColorRequest`/`EdgeColorMapping`, `EdgeColoringResult`. |
| **UI** | `UI/ComponentTreeDialog.cs`, `WinFormsDialogService.cs`, `IconStripFactory.cs` | WinForms modal TreeView (Spanish, icons, path display), dialog service, runtime icon-strip generation. |
| **COM utility** | `Infrastructure/ComRelease.cs` | Safe `Marshal.ReleaseComObject` wrapper for deterministic release of SW COM objects. |

**Key rule:** SolidWorks COM types appear **only** in `SwAddin.cs`, `CommandRegistrar.cs`,
and `SolidWorksService.cs` (including its partial `SolidWorksService.EdgeColoring.cs`).
Everything else works with plain models.

---

## 3. Ribbon registration — how the tabs appear (important)

This follows the **official SOLIDWORKS SDK pattern**: ONE command group holding every command,
plus ONE CommandManager tab per document type created with `AddCommandTab(docType, name)`.

`CommandRegistrar.Register()`:

1. `RemoveCommandGroup2(groupId, false)` — clear any leftover group from a previous load.
2. `CreateCommandGroup2(groupId, "Veradel", …, ignorePreviousVersion: true, ref errors)`
   — **always** `true`; reusing stale registry data gives `-1` command IDs and destabilises `Activate()`.
3. Set **opaque** PNG icon strips (`IconStripFactory`); one strip holds all command glyphs.
   Transparency / alpha destabilises `Activate()`, so icons are flat 24bpp.
4. `AddCommandItem2(name, -1, hint, tooltip, imageIndex:g, "RunCommand(g)", "EnableCommand(g)", userId, swMenuItem|swToolbarItem)`
   per command (g = global registry index).
5. `HasToolbar = true; HasMenu = true; Activate()`.
6. Resolve each command's ID via `get_CommandID(itemIndex)` (valid after `Activate`).
7. For each document type — Part, Assembly, Drawing — `AddCommandTab(swDocType, tabName)`
   (`"Veradel Pieza"` / `"Veradel Assembly"` / `"Veradel Dibujo"` from `RibbonIds.TabFor`),
   then `tab.AddCommandTabBox().AddCommands(ids, textTypes)` with **only the command IDs whose
   `ICommand.DocumentTypes` includes that type**.

**Why AddCommandTab, not ShowInDocumentType:** a tab created with `AddCommandTab(docType, …)` is
bound to that single document type, so it can NEVER leak into another type's ribbon. The
`swDocumentTypes_e` value passed is a single type (Part=1, Assembly=2, Drawing=3) — correct here
because it is not a bitmask. `ShowInDocumentType` only governs toolbars/menus (and spawns a stray
auto-tab), so it is deliberately not used. The earlier AddCommandTab failure was caused by the
`-1` command IDs (stale registry), now fixed by `ignorePreviousVersion: true`.

So "Abrir carpeta" (Part+Assembly+Drawing) gets a button in all three tabs; "Mostrar rosca"
(Drawing) only in "Veradel Dibujo".

**Callbacks are generic.** Button *g* calls `RunCommand(g)` / `EnableCommand(g)` on the
add-in; SwAddin forwards to `CompositionRoot.Execute(g)` / `CanExecute(g)`, which index into
the registry. The same command can have buttons in several tabs and still dispatch correctly.
Adding a command never touches the registrar.

---

## 4. "Abrir carpeta" (Open Folder) command behavior

Resolved in `OpenFolderCommand.Execute()`:

| Active document | Selection | Action |
|-----------------|-----------|--------|
| Part `.sldprt` | — | Reveal part file in Explorer (select file). |
| Drawing `.slddrw` | — | Reveal drawing file. |
| Assembly `.sldasm` | no component | Reveal assembly file. |
| Assembly `.sldasm` | component selected | Show modal TreeView (root = assembly → subassemblies → selected component). Pick a node → reveal its folder, or Cancel. |

Selection resolution uses `ISelectionMgr.GetSelectedObjectsComponent4` for **any** selected
entity (component, face, edge…), so selecting a face in the graphics area also works — it
returns the owning component.

**Edge cases (each logged):** unsaved doc (no path), virtual component (no external file),
suppressed/lightweight (still has a path; state shown), missing file (falls back to opening
the folder; error only if the folder is gone too).

### "Mostrar rosca" (cosmetic threads) — drawing-only

SolidWorks does not display cosmetic threads in assembly views by default; this command
imports them. `MostrarRoscaCommand` (tab **"Veradel Dibujo"**, drawing only):

- **CanExecute** → `ISolidWorksService.ActiveDrawingReferencesAssembly()`: enabled only when the
  active document is a drawing whose current sheet has at least one **assembly** view.
- **Execute** → `ISolidWorksService.InsertCosmeticThreadsInAssemblyViews()`: counts the **real**
  assembly views on the current sheet (template/orientation views named `*Front`, `*Top`,
  `*Isometric`, … are excluded), then makes a **single** call
  `IDrawingDoc.InsertModelAnnotations3(swImportModelItemsFromEntireModel, swInsertCThreads, AllViews:true, …)`.
  `AllViews = true` inserts into every view in one shot, so no per-view selection/loop is needed.
  Returns a COM-free `CosmeticThreadResult` (assembly views found / processed / failed / errors).
- **The fix that mattered (SW 2026):** the earlier "cannot convert NULL to bool" crash was caused
  by the wrong first argument (`-1`). With the correct `swImportModelItemsSource_e` /
  `swInsertAnnotation_e` values the call works — but note `InsertModelAnnotations3` returns
  `object` (the inserted annotations), so capture it as `object` and check `!= null`, never cast
  straight to `bool`.

### "Extraer pieza" (free + move a buried component) — assembly-only

For a component that ended up buried inside the assembly. `ExtraerPiezaCommand` (tab
"Veradel Assembly"; enabled only when a component is selected) warns the user with a single
Yes/No dialog summarising the changes, then `ISolidWorksService.ExtractSelectedComponent()`:
1. If `IComponent2.IsFixed()` → select it and `IAssemblyDoc.UnfixComponent()`.
2. Suppress its position mates: walk the feature tree (`FirstFeature`/`GetNextFeature` +
   sub-features), `Feature.GetSpecificFeature2() as IMate2`, and for each mate whose
   `MateEntity(i).ReferenceComponent` is the target, `Feature.SetSuppression2(swSuppressFeature, swThisConfiguration, null)`.
3. Move it past the assembly bounding box (+X): assembly box = union of
   `IComponent2.GetBox` over `IAssemblyDoc.GetComponents(true)`; shift the component's
   `Transform2.ArrayData[9]` (translation X) via `IMathUtility.CreateTransform` +
   `SetTransformAndSolve2`.
4. Re-select the moved component and `IModelDoc2.ViewZoomToSelection()` — zoom to the
   extracted component only (not the whole assembly), leaving it selected so the user sees it.
Returns a COM-free `ExtractComponentResult` (was-fixed / mates-suppressed / moved / error).

**Pattern/mirror guard:** before any change, `InspectSelectedComponentForExtraction()` flags
`IComponent2.IsPatternInstance() || IsMirrored()`. The command refuses such an instance with a
warning (moving it would break the pattern); `ExtractSelectedComponent()` re-checks as a safety net.

### "Captura de pantalla" (screenshot) — part & assembly

Grabs a real screenshot of the SolidWorks window and offers to save or copy it.
`CapturaPantallaCommand` (tabs "Veradel Pieza" + "Veradel Assembly"; enabled whenever a part or
assembly is active):

- **Execute** → `IScreenCaptureService.CaptureWindow(hwnd, leftInset)`: a **GDI screen capture**
  (not a model render), so on-screen **overlays drawn over the viewport are included** — dimensions,
  Measure callouts, menus. The capture is **cropped to the graphics area (model space)**:
  - The command gets the active view's window handle from
    `ISolidWorksService.GetActiveModelViewHandle()` (`IModelDoc2.IActiveView` →
    `IModelView.GetViewHWndx64()`). That window spans **FeatureManager + graphics** (the FM panel is
    drawn over its left edge), so capturing its `GetWindowRect` alone still showed the FeatureManager.
  - To drop the FeatureManager, the command also reads `ISolidWorksService.GetActiveFeatureManagerWidth()`
    (`IModelDoc2.GetFeatureManagerWidth()`) and passes it as `leftInset`; the capture service trims
    that many pixels off the **left** before `Graphics.CopyFromScreen`. No panel hiding / flicker.
  - Result is a **PNG** in `%TEMP%\VeradelAddin\` at the viewport's native resolution. Ribbon and
    toolbars are outside this window, so they never appear. Returns a COM-free `ScreenshotResult`.
  - Capture happens **before** the preview dialog is shown, while SolidWorks is foreground, so the
    add-in's own dialog never appears in the shot.
  - COM split: only `GetActiveModelViewHandle()` (in `SolidWorksService`) touches COM, returning a
    plain `IntPtr`; the GDI grab in `ScreenCaptureService` is COM-free. (The earlier
    `IModelDoc2.SaveBMP` approach was dropped — it renders only the model and omits overlays — and
    the full-window grab was narrowed to the viewport at the user's request.)
  - Note: a Measure dialog floating **over** the viewport is captured; if it is docked **outside**
    the graphics area it falls outside the crop. The measurement annotations on the model itself
    (in the viewport) are always captured.
- The command shows `IDialogService.ShowScreenshot(...)` → `ScreenshotDialog`: a modal preview of
  the image with **Guardar como** / **Copiar al portapapeles** / **Cerrar**. The preview bitmap is
  copied into memory so the temp file is never locked. **Copiar** puts the image on the clipboard
  (`Clipboard.SetImage`, a UI concern, done in the dialog). **Guardar como** collects a destination
  via `SaveFileDialog`; the command then copies the file with `IFileSystemService.CopyFile`.
- The temp PNG is always removed afterward via `IFileSystemService.TryDeleteFile` (in a `finally`).
- Returns `ScreenshotAction` (`Copied` / `Saved` / `None`) for per-branch logging.

### "Exportar" (PDF / DWG / STEP) — drawing-only

From a drawing, exports the drawing to PDF/DWG and/or the referenced 3D model to STEP.
`ExportarCommand` (tab "Veradel Dibujo"; enabled when a drawing is active). Two sequential dialogs:

1. **Formats** (`IDialogService.ShowExportOptions`) → `ExportOptionsDialog`: checkboxes PDF / DWG /
   STEP. STEP is **disabled with a reason** when not safe — driven by
   `ISolidWorksService.InspectActiveDrawingForExport()` → `DrawingExportInfo`.
2. **Prefix/suffix** (`IDialogService.ShowAffixPrompt(baseName, …)`) → `AffixDialog`: the **real** base
   name (e.g. `1720.020.000`, passed in from the drawing) is shown fixed in the middle with a textbox
   on each side — `[prefijo] 1720.020.000 [sufijo]` — so the user can add a prefix, a suffix, both or
   neither (empty side = nothing). Live filename preview using that name. Returns the two strings; the
   service builds `prefix + baseName + suffix` (each sanitised of invalid path chars).

Then `ISolidWorksService.ExportActiveDrawing(ExportRequest)`:
- **PDF / DWG** come from the active drawing: `IModelDocExtension.SaveAs3(path.pdf|.dwg, swSaveAsCurrentVersion, swSaveAsOptions_Silent, null, null, …)`, written next to the drawing file (name + affix).
- **STEP** comes from the **referenced model**, found by walking the drawing's views
  (`IDrawingDoc.GetFirstView` → `IView.GetNextView`, `IView.ReferencedDocument` /
  `GetReferencedModelName`). SolidWorks requires the doc to be active to export STEP, so the model is
  activated (`ISldWorks.ActivateDoc3(title, false, swDontRebuildActiveDoc, …)`), `SaveAs3(model.step)`
  is called, then the **drawing is reactivated**. If the model wasn't loaded it is opened
  (`OpenDoc6`, silent) and closed again afterwards.
- **Output location:** all three files go in the **drawing's folder**. PDF/DWG use the drawing's base
  name; the STEP keeps the model's own base name (so a part named differently from its drawing stays
  recognisable). The affix applies to all.
- **>10-component safety:** if the referenced model is an assembly with `IAssemblyDoc.GetComponentCount(false) > 10`,
  STEP is refused — both up front (greyed checkbox) and again inside `ExportActiveDrawing` as a safety net —
  because exporting a large assembly to STEP can crash SolidWorks.
- Returns a COM-free `ExportResult` (one `ExportItem` per format: success + path or error); the command
  logs the aggregate and shows a per-format summary.

### "Colorear aristas" / "Limpiar colores" — drawing-only (part drawings)

Carries the appearance colours of a part's **faces** onto the corresponding **edges** of its drawing.
`ColorearAristasCommand` + `LimpiarColoresCommand` (tab "Veradel Dibujo"). The whole algorithm is the
refactor of the legacy EdgeColoring code into the COM-isolated service: `SolidWorksService.EdgeColoring.cs`
(partial class). Commands stay thin.

- **Geometric principle:** a planar face whose normal is **perpendicular** to the view normal is seen
  edge-on, so its edges are candidate lines in that view.
- **Flow:** `InspectDrawingForEdgeColoring()` → `EdgeColoringPlan` (referenced part(s) + appearance
  colours via `IModelDocExtension.GetRenderMaterials2` → `RenderMaterial.PrimaryColor`). The dialog
  (`EdgeColoringDialog`, WinForms) lists each colour with a checkbox + target palette combo
  (6 colours, pre-suggested by **hue**). Before applying, an **experimental / may-be-slow** warning
  (`IDialogService.Confirm`) shows the part's face count (`EdgeColoringPlan.TotalFaceCount`, summed from
  `Body2.GetFaceCount`) and tells the user to save first. Then `ApplyEdgeColoring(EdgeColorRequest)`.
- **Per source colour:** gather the faces carrying that appearance (`RenderMaterial.GetEntities` →
  faces / feature faces / **whole bodies** (`Body2.GetFaces`) / components). If the appearance lists
  **no** specific entities it is **part-level**, so every visible body's faces are used
  (`PartDoc.GetBodies2(swAllBodies, true)`). Classify by `Surface.Identity()` (planar / cylinder /
  cone), compute face normals (`Face2.Normal`) and boxes (`Face2.GetBox`) and index them in a spatial hash grid.
  Per view (`IView.ModelToViewTransform` → view normal):
  - **Real view** (maps 3D→2D): edges of the candidate faces (isometric = all; orthographic = planar
    faces ⟂ view + cylinder + cone) → `IView.GetCorrespondingEntity` → select → `DrawingDoc.SetLineColor`.
  - **Synthetic view** (sections/detail): `IView.GetVisibleEntities2` edges, classify by adjacent
    faces, take the edge midpoint (`Edge.GetCurve` + `Curve.Evaluate2`/`ReverseEvaluate`) and look it up
    in the grid (`Face2.GetClosestPointOn` within tolerance) → select.
  - **Silhouettes** (curved-face outlines): a `swSelSILHOUETTES` selection filter + `SelectAll`, midpoint vs
    the cylinder **and cone** grids.
- **"Limpiar colores":** selects all edges + silhouettes (selection filter + `SelectAll`) and
  `SetLineColor(black)` to reset.
- **Best-effort:** the 3D→2D mapping is inherently partial (corresponding-entity gaps, sections,
  silhouettes), so it won't colour 100% of edges — `LimpiarColores` resets. **Bugs fixed vs. legacy:**
  `IsRealView` no longer crashes on views with < 5 edges (samples what's available); conical faces use
  their own grid; COM objects are released via `ComRelease`; **part-/body-level appearances are now
  honoured** (previously only per-face appearances coloured anything); **cone silhouettes** are coloured,
  not just cylinder ones; section views colour correctly (`SelectData.View` set, colour by real selection
  count). **API limit:** `IDrawingDoc` exposes only `SetLineColor(int)` — there is no "remove override"
  / "by-layer" reset, so `LimpiarColores` writes black (matches the legacy add-in).

---

### "Despiece de calderería" — drawing-only (part drawings), DESTRUCTIVE/experimental

Generates the boilermaking part breakdown of the part referenced by the active drawing, on **new
sheets**. `DespieceCalderiaCommand` (tab "Veradel Dibujo") → `SolidWorksService.Boilermaking.cs`
(partial, COM-isolated). Command stays thin (validate → config pick → destructive confirm → run → summary).

- **Validate / abort (logged):** active doc is a drawing; it references a **part** (assembly or no
  reference → abort); the part has bodies; a cut list exists (else fall back to the whole part as one
  item); new-sheet creation succeeds. "Doesn't fit on A0" is **not** an abort — it paginates.
- **Configuration:** `IModelDoc2.GetConfigurationNames`. >1 → mandatory `IDialogService.ChooseFromList`
  picker; exactly 1 → used silently. Activated with `ShowConfiguration2` before reading/drawing.
- **Weldment (DESTRUCTIVE):** multibody **and** not `IPartDoc.IsWeldment()` → `FeatureManager.
  InsertWeldmentFeature()` directly on the part + `ForceRebuild3` (no copy, no rollback — logged as
  irreversible; only with explicit confirmation). The part **and** drawing are `Save3`-d at the end.
- **Cut list:** walk features (`FirstFeature`/`GetNextFeature` + sub-features); each cut-list folder is
  a `BodyFolder` (`GetCutListType` ∈ {weldment, sheet-metal, solid}); per item → `GetBodies` (model
  bodies), `GetBodyCount` (qty), `Feature.CustomPropertyManager.Get5("Mark")` (mark, else running index).
- **3-view groups:** per item a base **front** `CreateDrawViewFromModelView3(model,"*Front",x,y,0)`
  with the bodies isolated via `IView.Bodies = Body2[]`; **side**/**top** by selecting the front
  (`ActivateView`) and `CreateUnfoldedViewAt3` (first angle: side right, top below). `Bodies` is set on
  **all three** (not assumed to inherit). Sheet-metal items also get a flat pattern panel
  (`CreateFlatPatternViewFromModelView3`, isolated to the item — best-effort).
- **Exploded view + balloons + table** (summary sheet): `AddPartExplodeStep(...,autoSpace,...)` if the
  config has none, isometric `CreateDrawViewFromModelView3(...,"*Isometric",...)` + `IView.ShowExploded`,
  own scale fitted from `GetOutline`/`ScaleDecimal`; `AutoBalloon5` (one per item, numbered from the
  table); cut-list table via `IView.InsertWeldmentTable` anchored bottom-right above the title block.
- **Scale & layout:** single global scale for all groups, chosen from a standard ladder
  (1:1…1:1000) so the largest group fits the A0 usable area. `IView.Position` = view **centre** in
  metres from the sheet **lower-left**. Uniform cells sized to the largest group (→ no overlaps);
  groups packed row-major, paginated onto additional **A0** sheets (`NewSheet4`, name `""` so SW
  auto-numbers, first sheet's `GetTemplateName` as the sheet format). Summary sheet first, then group
  sheets. Each group labelled "Marca N / Cant." via `InsertNote` + `SetTextPoint`.
- **Silent:** `ModelView.EnableGraphicsUpdate = false` + `ISldWorks.CommandInProgress = true` during bulk
  insertion (restored in `finally`).
- **Best-effort / simplifications (documented for iteration):** sheets default to **A0** (minimal
  A-size selection not yet implemented — pagination only); part auto-explode quality and per-body flat
  pattern isolation depend on the model and are warn-and-continue (not aborts); balloon number follows
  the cut-list table item order (= mark when the table is mark-numbered).

---

## 5. How to add a new command

1. **Create the class** under `Commands/`, implementing `ICommand`:
   ```csharp
   public sealed class ExportBomCommand : ICommand
   {
       private readonly ISolidWorksService _sw;
       private readonly ILogger _log;
       public ExportBomCommand(ISolidWorksService sw, ILogger log) { _sw = sw; _log = log; }

       public string Name    => "Exportar LDM";
       public string Tooltip => "Exporta la lista de materiales";
       public string Hint    => "Exporta la LDM del ensamblaje activo a CSV";
       // DocumentTypes decides which per-document-type tab(s) the button appears in.
       public IReadOnlyList<DocumentKind> DocumentTypes => new[] { DocumentKind.Assembly };
       public bool CanExecute() => _sw.GetActiveDocument()?.Kind == DocumentKind.Assembly;
       public void Execute() { /* log Success/Cancel; unhandled errors caught by dispatcher */ }
   }
   ```
2. **Register it** in `CompositionRoot` (the only edit to existing code):
   ```csharp
   _registry = new CommandRegistry()
       .Add(new OpenFolderCommand(swService, fsService, _dialog, Logger))
       .Add(new ExportBomCommand(swService, Logger));   // <-- new line
   ```
3. **Add the file** to `VeradeAddin.csproj`:
   `<Compile Include="Commands\ExportBomCommand.cs" />` (legacy csproj lists files explicitly).
4. Done. `DocumentTypes` decides which per-document-type tab(s) the button lands in — the
   registrar puts the command in the "Veradel Pieza"/"Veradel Assembly"/"Veradel Dibujo" tab for
   each kind listed. It auto-assigns the button, icon slot and `RunCommand(g)`/`EnableCommand(g)`
   callbacks; the dispatcher wraps it in the error safety net.
   **No change to `CommandRegistrar` or `SwAddin`.**

> Icons: `IconStripFactory` auto-generates one flat glyph per command. For real artwork,
> replace its output with opaque PNG strip file paths (sizes 20/32/40/64/96/128; the
> per-command strip is `size*commandCount` wide). Avoid alpha/transparency.

---

## 6. Logging pipeline & swapping the sink

```
Command ──► ILogger.Log(command, docType, outcome, detail, error)
                │  (Logger stamps DateTime.UtcNow, builds LogEntry)
                ▼
        for each ILogSink: sink.Write(entry)   (failures swallowed — logging never breaks a command)
                ▼
        JsonLinesFileSink → %APPDATA%\VeradelAddin\logs\veradel-addin-YYYYMMDD.jsonl
```

One JSON object per line: `timestamp` (UTC ISO-8601), `command`, `documentType`, `outcome`
(`Success`/`Cancel`/`Error`), `detail`, `error`.

**Swap/extend the sink (e.g. telemetry for sales usage stats):**
1. Implement `ILogSink` (e.g. `HttpTelemetrySink` posting `LogEntry`).
2. Register it in `CompositionRoot`:
   ```csharp
   Logger = new Logger(new ILogSink[]
   {
       new JsonLinesFileSink(),       // keep local file
       new HttpTelemetrySink(apiUrl)  // + send to analytics
   });
   ```
No command/service code changes — they depend only on `ILogger`.

---

## 7. COM lifetime

`SolidWorksService` releases the COM objects it creates (component chain, selection manager)
in a `finally` via `ComRelease.Release`, to avoid RCW build-up over long sessions. The active
document (`IActiveDoc2`) is intentionally **not** released. On disconnect, `CommandRegistrar`
removes the command group and releases the `ICommandManager`, and `SwAddin` releases `ISldWorks`
then forces a GC so SolidWorks can shut down cleanly.

---

## 8. Build, register, deploy

- **Build:** `MSBuild VeradeAddin.csproj /p:Configuration=Debug`.
  (MSBuild on this machine: `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`.)
- **Register with COM (run once, elevated):** Visual Studio *Register for COM interop*
  (`<RegisterForComInterop>true</RegisterForComInterop>` in Debug) runs `regasm` on build, **or** manually:
  `regasm /codebase bin\Debug\VeradeAddin.dll` (Developer Command Prompt as admin).
  This invokes `RegisterFunction`, writing:
  - `HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}` (default DWORD = 1, `Title`, `Description`)
  - `HKCU\Software\SolidWorks\AddInsStartup\{GUID}` (DWORD = 1)
- **Unregister:** `regasm /unregister bin\Debug\VeradeAddin.dll` → `UnregisterFunction` removes the keys.
- Add-in GUID: `8B3E2A14-6C9D-4F1A-9E2B-7A5C1D3F8E40`.
- ⚠️ `/codebase` points SW at the exact DLL path (`bin\Debug`); don't move/delete it. A Release
  build + proper installer is the next deployment step.

---

## 9. SolidWorks API verification & assumptions

All API calls compile against the installed `SolidWorks.Interop.*` assemblies (compiler-verified
signatures) and are confirmed working on the user's SolidWorks 2026:

- `ISldWorks`: `IActiveDoc2`, `SetAddinCallbackInfo2`, `GetCommandManager`.
- `ICommandManager`: `CreateCommandGroup2`, `RemoveCommandGroup2`, `GetCommandTab`,
  `AddCommandTab`, `RemoveCommandTab`.
- `ICommandGroup`: `AddCommandItem2`, `get_CommandID`, `IconList`, `MainIconList`,
  `HasToolbar`, `HasMenu`, `Activate`.
- `ICommandTab.AddCommandTabBox`; `ICommandTabBox.AddCommands`.
- `IModelDoc2`: `GetType`, `GetTitle`, `GetPathName`, `SelectionManager`, `Extension`,
  `ClearSelection2`.
- `IModelDocExtension.SelectByID2`.
- `ISelectionMgr`: `GetSelectedObjectCount2`, `GetSelectedObjectsComponent4`.
- `IComponent2`: `GetParent`, `Name2`, `GetPathName`, `IsVirtual`, `GetSuppression2`.
- `IDrawingDoc`: `GetCurrentSheet`, `InsertModelAnnotations3(swImportModelItemsFromEntireModel,
  swInsertCThreads, AllViews, …)` → returns **`object`** (not bool).
- `ISheet.GetViews`; `IView.Name` / `IView.ReferencedDocument` (cast to `IAssemblyDoc`).
- `IComponent2`: `IsFixed`, `Select4`, `GetMates`, `GetBox(false,false)` → `double[6]`,
  `Transform2` (get/set, `MathTransform.ArrayData` = 16 doubles, translation at [9],[10],[11]),
  `SetTransformAndSolve2`.
- `IAssemblyDoc`: `UnfixComponent`, `GetComponents(toplevelOnly)`.
- `IModelDoc2`: `ViewZoomToSelection()` → `Void` (zoom to current selection; used by "Extraer pieza").
- `IModelDoc2.IActiveView` → `IModelView`; `IModelView.GetViewHWndx64()` → `Int64` (graphics-area
  window handle, used to crop the screenshot to model space).
- `IModelDoc2.GetFeatureManagerWidth()` → `Int32` (FeatureManager panel width in px, trimmed off the
  left of the screenshot so only the graphics area remains).
- Export ("Exportar"): `IModelDocExtension.SaveAs3(name, version, options, exportData, advanced, ref errors, ref warnings)` → `Boolean`
  (format chosen by file extension: `.pdf` / `.dwg` / `.step`); `swSaveAsVersion_e.swSaveAsCurrentVersion = 0`,
  `swSaveAsOptions_e.swSaveAsOptions_Silent = 1`. `ISldWorks.ActivateDoc3(title, useUserPrefs, option, ref errors)`
  with `swRebuildOnActivation_e.swDontRebuildActiveDoc = 1`; `ISldWorks.OpenDoc6` / `CloseDoc`.
  `IDrawingDoc.GetFirstView`, `IView.GetNextView`, `IView.ReferencedDocument` / `GetReferencedModelName`.
  `IAssemblyDoc.GetComponentCount(toplevelOnly)` for the >10 STEP guard.
- Edge coloring ("Colorear aristas"): `IModelDocExtension.GetRenderMaterials2`,
  `RenderMaterial.PrimaryColor`/`GetEntities`/`FileName`; `Entity.GetType()` (→ swSelectType);
  `Feature.GetFaces`; `Face2.GetSurface`/`Normal`/`GetBox`/`GetEdges`/`GetClosestPointOn`;
  `Surface.Identity()` (`PLANE_TYPE=4001`/`CYLINDER_TYPE=4002`/`CONE_TYPE=4003`);
  `Edge.GetTwoAdjacentFaces2`/`GetCurve`/`GetStartVertex`/`GetEndVertex`; `Curve.Evaluate2`/`ReverseEvaluate`/`GetEndParams`;
  `IView.ModelToViewTransform`/`GetCorrespondingEntity`/`GetVisibleEntities2`/`GetVisibleComponents`/`GetOrientationName`/`GetBreakOutSectionCount`/`Type`;
  `IModelDocExtension.GetCorrespondingEntity2`/`SelectAll`; `IDrawingDoc.SetLineColor`;
  `ISldWorks.SetSelectionFilter`/`SetSelectionFilters`/`GetSelectionFilters`/`GetApplySelectionFilter`/`SetApplySelectionFilter`;
  `ISelectionMgr.AddSelectionListObject`/`CreateSelectData`/`SuspendSelectionList`/`ResumeSelectionList2`/`GetSelectedObject6`/`GetSelectedObjectType3`;
  `SilhouetteEdge.GetStartPoint`/`GetEndPoint`.
- Boilermaking breakdown ("Despiece de calderería"): `IModelDoc2.GetConfigurationNames`/`ShowConfiguration2`/`GetActiveConfiguration`/`FirstFeature`/`InsertNote`/`Save3(swSaveAsOptions_Silent=1)`;
  `IPartDoc.IsWeldment`/`GetBodies2(swAllBodies=-1, visibleOnly)`; `IFeatureManager.InsertWeldmentFeature`; `IModelDoc2.ForceRebuild3`;
  `IFeature.GetSpecificFeature2`/`GetFirstSubFeature`/`GetNextSubFeature`/`CustomPropertyManager`; `IBodyFolder.GetCutListType`(`swCutListType_e`: solid=1/sheetmetal=2/weldment=3)/`GetBodies`/`GetBodyCount`;
  `ICustomPropertyManager.Get5`; `IBody2.GetBodyBox`(→`double[6]`)/`IsSheetMetal`; `Entity.Select4`;
  `IConfiguration.GetNumberOfPartExplodeSteps`/`AddPartExplodeStep(view, dist, dirIdx, reverse, autoSpace, ref err)`;
  `IDrawingDoc.NewSheet4`(name `""`=auto, `swDwgPaperA0size=11`, `swDwgTemplateCustom=12`/`A0size=11`, firstAngle=true)/`GetSheetNames`/`ActivateSheet`/`GetCurrentSheet`/`GetFirstView`/`ActivateView`/`CreateDrawViewFromModelView3`/`CreateUnfoldedViewAt3`/`CreateFlatPatternViewFromModelView3`/`CreateAutoBalloonOptions`/`AutoBalloon5`;
  `ISheet.GetTemplateName`/`GetName`; `IView.Bodies`(set `Body2[]`)/`ScaleRatio`/`Position`(centre, m, from lower-left)/`GetOutline`/`ScaleDecimal`/`GetName2`/`ShowExploded`/`InsertWeldmentTable(useAnchor,x,y,anchorType,template,config)`;
  `IAutoBalloonOptions.Layout`(`swDetailingBalloonLayout_Circle=2`)/`Style`/`ItemNumberStart`; `INote.SetTextPoint`+`IAnnotation.SetPosition`;
  `ModelView.EnableGraphicsUpdate`+`ISldWorks.CommandInProgress` (silent). `IView.Bodies` per the *Set Body for View* example; child views do **not** inherit isolation → set on all 3.
- `IModelDoc2`: `FirstFeature`, `ViewZoomtofit2`. `IFeature`: `GetNextFeature`,
  `GetFirstSubFeature`/`GetNextSubFeature`, `GetSpecificFeature2`,
  `SetSuppression2(swSuppressFeature, swThisConfiguration, null)`.
- `IMate2`: `GetMateEntityCount`, `MateEntity(i)`; `IMateEntity2.ReferenceComponent`.
- `IMathUtility.CreateTransform(double[16])`; `ISldWorks.GetMathUtility`.
- All signatures above were read directly from the interop assembly by reflection (see
  [the API-verification memory]); e.g. this is how the `InsertModelAnnotations3 → object`
  return and the `swInsertCThreads = 1` / `swImportModelItemsFromEntireModel = 0` values were confirmed.

**Confirmed on SW 2026 (were the actual blockers):**
- Per-document-type tabs must use `AddCommandTab(docType, name)` (one tab bound to one type).
  `ShowInDocumentType` does NOT bind tabs to a type — tabs leaked into other document types — and
  its `swDocumentTypes_e`-as-bitmask is broken (`swDocDRAWING (3)` = `Part|Assembly`).
- `CreateCommandGroup2` must use `ignorePreviousVersion = true`; reusing registry data gave
  `-1` command IDs (→ empty/hidden tabs) and instability. With it fixed, `AddCommandTab` works.
- Icon strips must be **opaque** (no alpha); transparent PNG strips made `Activate()` drop the tab.

**Assumptions (verify against your SW version if behavior differs):**
- `IComponent2.GetParent()` returns `null` for a top-level component (direct child of the active
  assembly); the climb stops there and the active assembly becomes the tree root.
- Callback strings support an integer argument (`"RunCommand(0)"`) — how one pair of callbacks
  dispatches to N commands by index.
- Suppressed/lightweight components still return a usable `GetPathName()`; virtual components are
  detected via `IsVirtual` and treated as having no external file.
- `RegisterForComInterop` / `regasm` registration requires administrative rights.
```
