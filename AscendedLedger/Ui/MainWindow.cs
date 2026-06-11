using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AscendedLedger.Ui;

/// <summary>
/// Main plugin window. Currently a placeholder shell; market and
/// retainer-sales intelligence views land here as features are implemented.
/// </summary>
internal sealed class MainWindow : Window {
    private const string Title = "Ascended Ledger";
    private const float MinimumWidth = 480;
    private const float MinimumHeight = 320;

    internal MainWindow() : base(Title) {
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(MinimumWidth, MinimumHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw() {
        ImGui.TextWrapped("Ascended Ledger is bootstrapped. Market and retainer-sales intelligence features are under construction.");
    }
}
