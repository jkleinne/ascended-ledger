using AscendedLedger.Persistence;
using AscendedLedger.Services;
using AscendedLedger.Ui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AscendedLedger;

/// <summary>
/// Dalamud plugin entry point that owns plugin lifetime, windows, persisted
/// configuration, and the /ledger chat command. Acts as the composition root:
/// every service is constructed and wired here, then disposed in reverse order.
/// </summary>
public sealed class AscendedLedgerPlugin : IDalamudPlugin {
    private const string CommandName = "/ledger";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("AscendedLedger");
    private readonly PluginConfiguration configuration;
    private readonly TaxRateService taxRateService;
    private readonly RetainerMarketCaptureService marketCaptureService;
    private readonly RetainerHistoryCaptureService historyCaptureService;
    private readonly LedgerCoordinator coordinator;
    private readonly MainWindow mainWindow;

    /// <summary>
    /// Initializes the plugin with Dalamud services, capture pipeline, store,
    /// the main window, and the chat command.
    /// </summary>
    /// <param name="pluginInterface">Plugin interface supplied by Dalamud at load.</param>
    /// <param name="commandManager">Chat command registry used for the /ledger command.</param>
    /// <param name="addonLifecycle">Addon lifecycle events driving listings capture.</param>
    /// <param name="gameInterop">Hooking provider for the sale-history capture.</param>
    /// <param name="marketBoard">Market board events supplying live tax rates.</param>
    /// <param name="playerState">Local character identity for multi-character keying.</param>
    /// <param name="framework">Frame ticks driving debounced ledger saves.</param>
    /// <param name="dataManager">Game sheets for item-name resolution.</param>
    /// <param name="log">Plugin log sink.</param>
    /// <param name="chat">Chat output for one-time recovery and failure notices.</param>
    public AscendedLedgerPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IAddonLifecycle addonLifecycle,
        IGameInteropProvider gameInterop,
        IMarketBoard marketBoard,
        IPlayerState playerState,
        IFramework framework,
        IDataManager dataManager,
        IPluginLog log,
        IChatGui chat) {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        var loadedConfiguration = pluginInterface.GetPluginConfig() as PluginConfiguration;
        configuration = loadedConfiguration ?? new PluginConfiguration();
        if (loadedConfiguration is null) {
            pluginInterface.SavePluginConfig(configuration);
        }

        taxRateService = new TaxRateService(marketBoard, log);
        marketCaptureService = new RetainerMarketCaptureService(addonLifecycle, playerState, log);
        historyCaptureService = new RetainerHistoryCaptureService(gameInterop, log);
        var store = new JsonLedgerStore(pluginInterface.GetPluginConfigDirectory(), log);
        coordinator = new LedgerCoordinator(store, taxRateService, marketCaptureService, historyCaptureService, playerState, framework, log, chat);

        mainWindow = new MainWindow(coordinator, new ItemNameResolver(dataManager));
        windowSystem.AddWindow(mainWindow);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Open the Ascended Ledger window.",
        });
    }

    /// <summary>
    /// Releases Dalamud subscriptions, the capture pipeline (flushing a final
    /// ledger save), and the chat command registration.
    /// </summary>
    public void Dispose() {
        commandManager.RemoveHandler(CommandName);
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        windowSystem.RemoveAllWindows();
        coordinator.Dispose();
        historyCaptureService.Dispose();
        marketCaptureService.Dispose();
        taxRateService.Dispose();
    }

    private void OnCommand(string command, string arguments) => OpenMainUi();

    private void OpenMainUi() => mainWindow.IsOpen = true;
}
