using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Xande.TestPlugin.Windows;

namespace Xande.TestPlugin;

public class Plugin : IDalamudPlugin {
    public string Name => "Xande.TestPlugin";

    public static Configuration Configuration { get; private set; } = null!;
    private static   WindowSystem _windowSystem = new("Xande.TestPlugin");
    private readonly MainWindow   _mainWindow;

    public Plugin( DalamudPluginInterface pluginInterface ) {
        pluginInterface.Create< Service >();

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Save();

        _mainWindow = new MainWindow();
        _windowSystem.AddWindow( _mainWindow );

        Service.CommandManager.AddHandler( "/xande", new CommandInfo( OnCommand ) {
            HelpMessage = "Open the test menu",
        } );

        Service.PluginInterface.UiBuilder.Draw         += DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenUi;
    }

    public void Dispose() {
        _mainWindow.Dispose();
        _windowSystem.RemoveAllWindows();

        Service.CommandManager.RemoveHandler( "/xande" );

        Service.PluginInterface.UiBuilder.Draw         -= DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
    }

    private void OnCommand( string command, string args ) {
        OpenUi();
    }

    private void OpenUi() {
        _mainWindow.IsOpen = true;
    }

    private void DrawUi() {
        _windowSystem.Draw();
    }
}