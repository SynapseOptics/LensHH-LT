using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;

namespace LensHH.App.ViewModels;

/// <summary>The three model-glass (fictitious glass) parameters this bulk tool can vary/bound. Each
/// maps to a distinct storage on <see cref="LensHH.Core.Models.Surface"/>: Nd → ModelNdVariable/Min/Max;
/// Vd → ModelVdVariable/Min/Max; dPgF → ModelDPgFVariable/Min/Max. All are only meaningful on a
/// surface whose <see cref="LensHH.Core.Models.Surface.ModelIndexEnabled"/> is set.</summary>
public enum ModelGlassParam { Nd, Vd, DPgF }

/// <summary>
/// Bulk "Model Glass Constraints" dialog (Variable Editor). Sets a chosen model-glass parameter
/// (Nd, Vd, or dPgF) as an optimization variable — with an optional Min/Max bound — across a surface
/// range, applying only to surfaces already in model-index mode. Mirrors the Surface Parameter
/// Constraints dialog, and gives the model-glass parameters the same one-step bulk set-variable +
/// bound treatment the surface-parameter and thickness/curvature families already have.
/// </summary>
public partial class ModelGlassConstraintViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public string Title => "Model Glass Constraints";

    // Variable type combo — which model-glass parameter to edit.
    public List<string> VariableTypeOptions { get; } = new() { "Nd", "Vd", "dPgF" };
    [ObservableProperty] private int _variableTypeIndex;  // default Nd

    [ObservableProperty] private int _surface1;
    [ObservableProperty] private int _surface2;

    // Constraint type — bounds to apply (or Clear = make fixed / remove the variable).
    public List<string> ConstraintOptions { get; } = new()
        { "Variable (no bounds)", "Min/Max", "Max Only", "Min Only", "Clear (make fixed)" };
    [ObservableProperty] private int _constraintTypeIndex = 1;  // default Min/Max

    [ObservableProperty] private string _minText = "";
    [ObservableProperty] private string _maxText = "";

    public bool IsMinEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 3;
    public bool IsMaxEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 2;

    public ModelGlassConstraintViewModel(GuiSession session)
    {
        _session = session;
        _surface1 = 1;
        _surface2 = Math.Max(1, session.System.Surfaces.Count - 2); // last before image
    }

    partial void OnConstraintTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMinEnabled));
        OnPropertyChanged(nameof(IsMaxEnabled));
    }

    public void Apply()
    {
        int s1 = Math.Max(0, Surface1);
        int s2 = Math.Min(_session.System.Surfaces.Count - 1, Surface2);
        var kind = (ModelGlassParam)VariableTypeIndex;
        bool clear = ConstraintTypeIndex == 4;
        bool setVar = !clear;

        double? min = null, max = null;
        if (!clear)
        {
            if (IsMinEnabled && double.TryParse(MinText, out double mv)) min = mv;
            if (IsMaxEnabled && double.TryParse(MaxText, out double xv)) max = xv;
        }

        for (int i = s1; i <= s2; i++)
        {
            var surf = _session.System.Surfaces[i];
            // Model-glass parameters are only meaningful on a model-index surface; a real catalog
            // glass has no Nd/Vd/dPgF variable, so skip it rather than silently convert it.
            if (!surf.ModelIndexEnabled) continue;

            switch (kind)
            {
                case ModelGlassParam.Nd:
                    surf.ModelNdVariable = setVar;
                    surf.ModelNdMin = min;
                    surf.ModelNdMax = max;
                    break;
                case ModelGlassParam.Vd:
                    surf.ModelVdVariable = setVar;
                    surf.ModelVdMin = min;
                    surf.ModelVdMax = max;
                    break;
                case ModelGlassParam.DPgF:
                    surf.ModelDPgFVariable = setVar;
                    surf.ModelDPgFMin = min;
                    surf.ModelDPgFMax = max;
                    break;
            }
        }

        _session.NotifySystemChanged("properties");
    }
}
