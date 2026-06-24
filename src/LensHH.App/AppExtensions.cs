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
    }
}
