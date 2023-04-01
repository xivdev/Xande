using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;

namespace Xande.TestPlugin;

public class Service {
    [PluginService]
    public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static DataManager DataManager { get; private set; } = null!;

    [PluginService]
    public static Framework Framework { get; private set; } = null!;

    [PluginService]
    public static CommandManager CommandManager { get; private set; } = null!;
}