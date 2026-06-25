# Documentación API SolidWorks — Veradel Addin

Referencia de **todos** los comandos de la API de SolidWorks que usa el add-in. Un archivo por
interfaz. Cada archivo sigue la misma estructura: Resumen · Accesors · Casos de uso · Propiedades · Métodos.

Las medidas de la API de SolidWorks están **siempre en metros y radianes** (no mm/grados).

## Interfaces

| Interfaz | Qué es | Estado |
|----------|--------|--------|
| [ModelDoc2](ModelDoc2.md) | Documento abierto (común a pieza/ensamblaje/plano) | ✅ iniciado |
| ModelDocExtension | Extensión de `ModelDoc2` (selección por id, chaflanes…) | ⬜ pendiente |
| [PartDoc](PartDoc.md) | Documento de pieza | ⬜ pendiente |
| AssemblyDoc | Documento de ensamblaje | ⬜ pendiente |
| DrawingDoc | Documento de plano | ⬜ pendiente |
| SketchManager | Creación de croquis y entidades de croquis | ⬜ pendiente |
| Sketch | Croquis activo (`ConstrainAll`…) | ⬜ pendiente |
| FeatureManager | Creación de operaciones (revolución, chaflán…) | ⬜ pendiente |
| Feature / SketchSegment / SketchLine / SketchPoint | Entidades seleccionables | ⬜ pendiente |
| SldWorks | Aplicación SolidWorks | ⬜ pendiente |
| CommandManager / CommandGroup | Cinta de comandos del add-in | ⬜ pendiente |

> Las descripciones de parámetros marcadas con **(oficial pendiente)** son paráfrasis; si quieres el
> texto literal de la ayuda de SolidWorks API, pégamelo y lo sustituyo.
