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

        /// <summary>Product name shown in the title bar and About box. Default "LensHH-LT".</summary>
        public static string ProductName { get; set; } = "LensHH-LT";

        /// <summary>
        /// Version string shown in the About box. When null (default) the About box falls back
        /// to this assembly's version. A host whose own assembly carries the product version
        /// should set this so the About box reports the host's version, not the shared app's.
        /// </summary>
        public static string? ProductVersion { get; set; }

        /// <summary>One-line product description shown in the About box.</summary>
        public static string ProductTagline { get; set; } = "Optical Lens Design Tool";

        /// <summary>
        /// Optional extra block shown in the About box (e.g. an edition's feature summary).
        /// Empty in the standard build. A host may set this to describe its edition.
        /// </summary>
        public static string AboutDetails { get; set; } = "";

        /// <summary>
        /// Extension/format used for native Save and Save As (without the dot). Default
        /// "lhlt". A host with an extended native format sets this to its own extension, and
        /// every save is written in that format.
        /// </summary>
        public static string NativeFileExtension { get; set; } = "lhlt";

        /// <summary>
        /// Native extensions (without the dot) offered in the Open dialog. Default just
        /// "lhlt". A host that can also open an extended format lists both.
        /// </summary>
        public static string[] NativeOpenExtensions { get; set; } = { "lhlt" };
    }
}
