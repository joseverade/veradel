namespace VeradeAddin.Models
{
    /// <summary>Parts the "Configurar pieza" catalog can generate. Add new kinds here.</summary>
    public enum ConfigurablePart
    {
        None = 0,
        Bolt = 1
    }

    /// <summary>
    /// What the user picked and configured in the "Configurar pieza" dialog: the chosen part kind
    /// plus the relevant spec. Only the spec matching <see cref="Part"/> is populated.
    /// </summary>
    public sealed class PartConfiguratorSelection
    {
        public ConfigurablePart Part { get; set; }

        /// <summary>Set when <see cref="Part"/> is <see cref="ConfigurablePart.Bolt"/>.</summary>
        public BoltSpec Bolt { get; set; }
    }
}
