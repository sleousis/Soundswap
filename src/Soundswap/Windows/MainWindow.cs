using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Soundswap.Windows;

/// <summary>The one window. Pages switch inside it rather than opening more windows.</summary>
public sealed class MainWindow : Window
{
    private readonly Configuration config;
    private readonly Action save;

    public MainWindow(Configuration config, Action save) : base("Soundswap###SoundswapMain")
    {
        this.config = config;
        this.save = save;
        Size = new Vector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 260), MaximumSize = new Vector2(4000, 3000) };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Hello from Soundswap.");
        ImGui.Spacing();

        var still = config.ReduceMotion;
        if (ImGui.Checkbox("Hold still", ref still))
        {
            config.ReduceMotion = still;
            save();
        }

        using (ImRaii.Disabled(true))
            ImGui.TextWrapped("Replace this window with the plugin's real first page. See the dalamud:ui skill.");
    }
}
