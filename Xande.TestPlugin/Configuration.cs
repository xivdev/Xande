using Dalamud.Configuration;

namespace Xande.TestPlugin;

[Serializable]
public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;

    public bool AutoOpen { get; set; } = false;

    public Dictionary< string, string > ResolverOverrides = new() {
        { "this_path_will_never_appear", "this_path_will_never_appear" }
    };

    public void Save() {
        Service.PluginInterface.SavePluginConfig( this );
    }
}