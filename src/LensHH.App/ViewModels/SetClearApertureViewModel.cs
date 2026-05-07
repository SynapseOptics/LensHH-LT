using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;

namespace LensHH.App.ViewModels;

public partial class SetClearApertureViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private int _surface1 = 1;
    [ObservableProperty] private int _surface2 = 1;
    [ObservableProperty] private double _caPercent = 100.0;
    [ObservableProperty] private bool _allFixed;
    [ObservableProperty] private bool _allAuto;

    // Mutual exclusion: only one of AllFixed / AllAuto can be checked at
    // a time. The guard inside each handler prevents infinite recursion
    // when the partner toggle flips back to false.
    partial void OnAllFixedChanged(bool value) { if (value) AllAuto = false; }
    partial void OnAllAutoChanged(bool value)  { if (value) AllFixed = false; }

    public SetClearApertureViewModel(GuiSession session)
    {
        _session = session;
        _surface2 = session.System.Surfaces.Count - 2; // default to last optical surface
    }

    public void Apply()
    {
        int s1 = System.Math.Max(0, Surface1);
        int s2 = System.Math.Min(_session.System.Surfaces.Count - 1, Surface2);

        // Apply the mode change first so the CA% loop below sees the new
        // mode. Stop surface stays Auto regardless.
        if (AllFixed || AllAuto)
        {
            var newMode = AllFixed ? SemiDiameterMode.Fixed : SemiDiameterMode.Auto;
            for (int i = s1; i <= s2; i++)
            {
                var surf = _session.System.Surfaces[i];
                if (!surf.IsStop)
                    surf.SemiDiameterMode = newMode;
            }
        }

        for (int i = s1; i <= s2; i++)
        {
            var surf = _session.System.Surfaces[i];
            // Only apply CA% to Auto (non-fixed) and non-stop surfaces
            if (surf.SemiDiameterMode == SemiDiameterMode.Auto && !surf.IsStop)
            {
                surf.ClearAperturePercent = CaPercent;
            }
        }

        _session.NotifySystemChanged("surface");
    }
}
