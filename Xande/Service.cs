using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace Xande;

public class Service {
    [PluginService]
    public required IGameInteropProvider GameInteropProvider { get; set; }
}