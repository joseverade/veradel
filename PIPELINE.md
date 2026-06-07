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
| **Models** | `Models/*` | COM-free DTOs: `ActiveDocument`, `ComponentNode`, `DocumentKind`, `CosmeticThreadResult`, `ComponentExtractionPlan`, `ExtractComponentResult`, `ScreenshotResult`, `ScreenshotAction`, `DrawingExportInfo`, `ExportRequest`, `ExportResult`/`ExportItem`. |
| **UI** | `UI/ComponentTreeDialog.cs`, `WinFormsDialogService.cs`, `IconStripFactory.cs` | WinForms modal TreeView (Spanish, icons, path display), dialog service, runtime icon-strip generation. |
| **COM utility** | `Infrastructure/ComRelease.cs` | Safe `Marshal.ReleaseComObject` wrapper for deterministic release of SW COM objects. |

**Key rule:** SolidWorks COM types appear **only** in `SwAddin.cs`, `CommandRegistrar.cs`,
and `SolidWorksService.cs`. Everything else works with plain models.

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
