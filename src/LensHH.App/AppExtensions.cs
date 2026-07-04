using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using LensHH.App.Session;

namespace LensHH.App
{
    /// <summary>
    /// One host-contributed menu entry, shown under the neutral "Extensions" menu.
    /// <see cref="Invoke"/> receives the owning window (for modal dialogs) and the live
    /// <see cref="GuiSession"/> (the currently loaded system, merit, glass catalog).
    /// </summary>
    public sealed class ExtensionMenuItem
    {
        public string Header { get; set; } = "";
        public Func<Window, GuiSession, Task> Invoke { get; set; } = (_, _) => Task.CompletedTask;
    }

    /// <summary>
    /// Optional extension points for a host application. A host may populate
    /// <see cref="MenuItems"/> before the main window is constructed; the window then renders
    /// them under a neutral "Extensions" menu (which stays hidden when the list is empty).
    /// The standard build registers nothing, so the menu does not appear.
    /// </summary>
    public static class AppExtensions
    {
        /// <summary>Header of the host-extension top-level menu (shown only when
        /// <see cref="MenuItems"/> is non-empty). "_" marks the access key.</summary>
        public static string MenuHeader { get; set; } = "E_xtensions";

        public static List<ExtensionMenuItem> MenuItems { get; } = new();

        /// <summary>
        /// Optional factory for the Edit → Fields dialog. When set, the main window uses it
        /// instead of the built-in field editor — letting a host supply its own editor (e.g.
        /// one with extra columns) bound to the live <see cref="GuiSession"/>. Null in the
        /// standard build, so the built-in field editor is used.
        /// </summary>
        public static Func<GuiSession, Window>? FieldEditorFactory { get; set; }

        /// <summary>
        /// Optional multi-configuration navigator. When set (an advanced-edition host has a
        /// multi-config design), the main window shows a Prev/Next Config control bound to it;
        /// when null (standard build) there is no config control. The interface is neutral — it
        /// exposes no edition-specific types — so the shared app carries zero multi-config code.
        /// </summary>
        public static IConfigNavigator? ConfigNavigator { get; set; }

        /// <summary>
        /// Optional cell-ownership provider. When set, the shared surface grid renders cells the
        /// provider reports as varied-across-configs read-only + italic; when null (standard
        /// build) nothing is owned and every cell is editable.
        /// </summary>
        public static ICellOwnershipProvider? CellOwnership { get; set; }
    }

    /// <summary>
    /// Neutral configuration-navigation seam. An advanced-edition host implements it over its
    /// multi-configuration editor; the shared main window binds the Prev/Next Config control to
    /// it. Switching configuration is ONE atomic transaction that raises <see cref="Changed"/>
    /// exactly once (never per-surface) so the UI refreshes with a single view re-derive.
    /// </summary>
    public interface IConfigNavigator
    {
        /// <summary>Active configuration, 1-based for display.</summary>
        int Current { get; }
        /// <summary>Total number of configurations.</summary>
        int Count { get; }
        /// <summary>Switch to the previous configuration (no-op at the first).</summary>
        void Prev();
        /// <summary>Switch to the next configuration (no-op at the last).</summary>
        void Next();
        /// <summary>
        /// Raised once after any change that affects navigation or cell ownership — a config
        /// switch OR an operand add/remove. The host coalesces the underlying model writes so
        /// this fires a single time; the main window responds with one view re-derive.
        /// </summary>
        event Action? Changed;
    }

    /// <summary>Surface parameters that can be varied across configurations.</summary>
    public enum SurfaceConfigParam { Curvature, Thickness, Glass, Conic, SemiDiameter }

    /// <summary>
    /// Neutral cell-ownership seam. An advanced-edition host implements it over its
    /// multi-configuration editor; the shared surface grid renders owned cells read-only +
    /// italic. Ownership is DERIVED (a pure query), never a stored flag — so it cannot drift.
    /// </summary>
    public interface ICellOwnershipProvider
    {
        /// <summary>True if this surface parameter varies across configurations (MCE-owned).</summary>
        bool IsVariedAcrossConfigs(int surfaceIndex, SurfaceConfigParam param);
    }
}
