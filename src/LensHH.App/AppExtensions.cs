using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using LensHH.App.Session;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using LensHH.Core.Optimization;

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
        /// Invoked once, right after the <see cref="GuiSession"/> is created and before the main
        /// window is constructed. An advanced-edition host binds its per-session providers here
        /// (e.g. sets <see cref="ConfigNavigator"/> / <see cref="CellOwnership"/> bound to this
        /// session). Null in the standard build.
        /// </summary>
        public static Action<GuiSession>? SessionCreated { get; set; }

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

        /// <summary>
        /// Optional provider of config-specific optimization variables (advanced edition). When set,
        /// the shared Variable Editor lists them (with editable bounds) alongside the base surface
        /// variables; null (standard build) → only base variables are shown.
        /// </summary>
        public static IConfigVariableProvider? ConfigVariables { get; set; }

        /// <summary>
        /// Optional optimizer factory. When set (an advanced-edition host with multi-configuration
        /// designs), the shared optimize dialogs build their <see cref="LocalOptimizer"/> through it
        /// so the host can pass its multi-configuration editor at CONSTRUCTION (the merit evaluator
        /// binds the editor there — it cannot be attached afterwards). Null in the standard build →
        /// the plain three-argument constructor is used. The signature exposes no edition-specific
        /// types, so the shared app carries zero multi-config code.
        /// </summary>
        public static Func<OpticalSystem, LensHH.Core.MeritFunction.MeritFunction, GlassCatalogManager, LocalOptimizer>? LocalOptimizerFactory { get; set; }

        /// <summary>Construct a LocalOptimizer via <see cref="LocalOptimizerFactory"/> when set, else the plain constructor.</summary>
        internal static LocalOptimizer CreateLocalOptimizer(OpticalSystem system,
            LensHH.Core.MeritFunction.MeritFunction meritFunction, GlassCatalogManager glassMgr)
            => LocalOptimizerFactory?.Invoke(system, meritFunction, glassMgr)
               ?? new LocalOptimizer(system, meritFunction, glassMgr);

        /// <summary>Merit evaluator factory: an advanced-edition host injects its multi-configuration
        /// editor so the Merit tab's Evaluate/Value sums every configuration (each operand in its own
        /// config). Null in the standard build → the plain single-config evaluator.</summary>
        public static Func<OpticalSystem, GlassCatalogManager, LensHH.Core.MeritFunction.MeritFunctionEvaluator>? MeritEvaluatorFactory { get; set; }

        /// <summary>Construct a MeritFunctionEvaluator via <see cref="MeritEvaluatorFactory"/> when set, else the plain constructor.</summary>
        internal static LensHH.Core.MeritFunction.MeritFunctionEvaluator CreateMeritEvaluator(OpticalSystem system, GlassCatalogManager glassMgr)
            => MeritEvaluatorFactory?.Invoke(system, glassMgr)
               ?? new LensHH.Core.MeritFunction.MeritFunctionEvaluator(system, glassMgr);

        // Sibling factories for the global-search optimizers, same contract as LocalOptimizerFactory:
        // null in the standard build; an advanced-edition host injects its multi-configuration editor
        // at construction. The optimizers themselves already accept it; these seams route it through.
        public static Func<OpticalSystem, LensHH.Core.MeritFunction.MeritFunction, GlassCatalogManager, MultistartOptimizer>? MultistartOptimizerFactory { get; set; }
        public static Func<OpticalSystem, LensHH.Core.MeritFunction.MeritFunction, GlassCatalogManager, BasinHoppingOptimizer>? BasinHoppingOptimizerFactory { get; set; }
        public static Func<OpticalSystem, LensHH.Core.MeritFunction.MeritFunction, GlassCatalogManager, BasinHoppingOptimizerBatch>? BasinHoppingOptimizerBatchFactory { get; set; }
        public static Func<OpticalSystem, LensHH.Core.MeritFunction.MeritFunction, GlassCatalogManager, GlobalBasinHoppingOptimizer>? GlobalBasinHoppingOptimizerFactory { get; set; }

        internal static MultistartOptimizer CreateMultistartOptimizer(OpticalSystem system,
            LensHH.Core.MeritFunction.MeritFunction mf, GlassCatalogManager glass)
            => MultistartOptimizerFactory?.Invoke(system, mf, glass) ?? new MultistartOptimizer(system, mf, glass);

        internal static BasinHoppingOptimizer CreateBasinHoppingOptimizer(OpticalSystem system,
            LensHH.Core.MeritFunction.MeritFunction mf, GlassCatalogManager glass)
            => BasinHoppingOptimizerFactory?.Invoke(system, mf, glass) ?? new BasinHoppingOptimizer(system, mf, glass);

        internal static BasinHoppingOptimizerBatch CreateBasinHoppingOptimizerBatch(OpticalSystem system,
            LensHH.Core.MeritFunction.MeritFunction mf, GlassCatalogManager glass)
            => BasinHoppingOptimizerBatchFactory?.Invoke(system, mf, glass) ?? new BasinHoppingOptimizerBatch(system, mf, glass);

        internal static GlobalBasinHoppingOptimizer CreateGlobalBasinHoppingOptimizer(OpticalSystem system,
            LensHH.Core.MeritFunction.MeritFunction mf, GlassCatalogManager glass)
            => GlobalBasinHoppingOptimizerFactory?.Invoke(system, mf, glass) ?? new GlobalBasinHoppingOptimizer(system, mf, glass);
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

        /// <summary>Solve marker for an owned parameter in the ACTIVE configuration: " V" (variable),
        /// " P" (pickup), or "" (fixed / not owned). The Lens Editor appends it to the owned cell so
        /// the user sees the solve of the value currently shown.</summary>
        string ConfigSolveMarker(int surfaceIndex, SurfaceConfigParam param);
    }

    /// <summary>
    /// A config-specific optimization variable surfaced to the shared Variable Editor (advanced
    /// edition). Bounds are read/written through delegates, so the editor needs no knowledge of the
    /// multi-configuration model — it just shows a row and edits Min/Max like any other variable.
    /// </summary>
    public sealed class ConfigVariableInfo
    {
        public string Description { get; init; } = "";     // e.g. "S6 Thickness [Config 2]"
        public int SurfaceIndex { get; init; }
        public System.Func<double?> GetMin { get; init; } = () => null;
        public System.Func<double?> GetMax { get; init; } = () => null;
        public System.Action<double?> SetMin { get; init; } = _ => { };
        public System.Action<double?> SetMax { get; init; } = _ => { };
    }

    /// <summary>Neutral seam: an advanced-edition host enumerates its config-specific optimization
    /// variables (with bound accessors) so the shared Variable Editor can list + edit them. Null in
    /// the standard build → the editor shows only base surface variables.</summary>
    public interface IConfigVariableProvider
    {
        System.Collections.Generic.IReadOnlyList<ConfigVariableInfo> GetConfigVariables();
    }
}
