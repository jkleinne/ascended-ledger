using AscendedLedger.Ui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AscendedLedger;

/// <summary>
/// Dalamud plugin entry point that owns plugin lifetime, windows, persisted
/// configuration, and the /ledger chat command.
/// </summary>
public sealed class AscendedLedgerPlugin : IDalamudPlugin {
    private const string CommandName = "/ledger";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("AscendedLedger");
    private readonly PluginConfiguration configuration;
    private readonly MainWindow mainWindow;

    /// <summary>
    /// Initializes the plugin with Dalamud services, the main window, and the chat command.
    /// </summary>
    /// <param name="pluginInterface">Plugin interface supplied by Dalamud at load.</param>
    /// <param name="commandManager">Chat command registry used for the /ledger command.</param>
    public AscendedLedgerPlugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager) {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        var loadedConfiguration = pluginInterface.GetPluginConfig() as PluginConfiguration;
        configuration = loadedConfiguration ?? new PluginConfiguration();
        if (loadedConfiguration is null) {
            pluginInterface.SavePluginConfig(configuration);
        }

        mainWindow = new MainWindow();
        windowSystem.AddWindow(mainWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Open the Ascended Ledger window.",
        });
    }

    /// <summary>
    /// Releases Dalamud subscriptions and the chat command registration.
    /// </summary>
    public void Dispose() {
        commandManager.RemoveHandler(CommandName);
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string arguments) => OpenMainUi();

    private void OpenMainUi() => mainWindow.IsOpen = true;
}
