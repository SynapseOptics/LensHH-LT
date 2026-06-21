namespace LensHH.App
{
    /// <summary>
    /// Product-neutral capability switches a host can set at startup to tailor the shared app
    /// before the main window loads. Defaults reflect the standard build; an alternate host may
    /// override any of these in its own entry point.
    /// </summary>
    public static class AppCapabilities
    {
        /// <summary>
        /// Whether the in-app self-service trial start is offered. Default <c>true</c>.
        /// A host that activates only via externally provided, signed token files sets this
        /// <c>false</c>, which hides the "Start Free Trial" menu item and leaves activation
        /// to the standard "Activate" / load-token-file paths only.
        /// </summary>
        public static bool TrialStartSupported { get; set; } = true;
    }
}
