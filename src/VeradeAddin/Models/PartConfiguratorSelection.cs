namespace VeradeAddin.Models
{
    /// <summary>
    /// Parts the "Configurar pieza" catalog can generate. Both kinds are built by the SHAFT engine
    /// (a bolt is a 2-level shaft wizard); the kind only records which catalog card / wizard mode
    /// produced the spec, so a re-edit reopens the right wizard.
    /// </summary>
    public enum ConfigurablePart
    {
        None = 0,
        Bolt = 1,
        Shaft = 2
    }

    /// <summary>
    /// What the user picked and configured in the "Configurar pieza" dialog: the chosen part kind,
    /// the shaft spec that builds it (both kinds revolve through the shaft engine) and the raw
    /// pipe-delimited page message, stored verbatim in the part registry so the wizard can be
    /// reopened later with the same values.
    /// </summary>
    public sealed class PartConfiguratorSelection
    {
        public ConfigurablePart Part { get; set; }

        /// <summary>Geometry spec; set for both kinds (a bolt is a 2-level shaft).</summary>
        public ShaftSpec Shaft { get; set; }

        /// <summary>The raw <c>create|shaft|S|...</c> / <c>create|shaft|B|...</c> message as posted by the page.</summary>
        public string RawMessage { get; set; }
    }
}
