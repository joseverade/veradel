namespace VeradeAddin.Models
{
    /// <summary>
    /// What the user right-clicks for a context-toolbar icon to appear, decoupled from the COM enum
    /// <c>swSelectType_e</c> (mapped by <c>ContextToolbarRegistrar</c>).
    /// </summary>
    public enum ContextToolbarTarget
    {
        /// <summary>A component, in the FeatureManager tree or in the graphics area.</summary>
        Component,

        /// <summary>Empty space in the graphics area (nothing selected).</summary>
        EmptySpace
    }
}
