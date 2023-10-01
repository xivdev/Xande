using Dalamud.Logging;
using Dalamud.Plugin;
using Lumina.Data;
using Lumina.Data.Files;
using Penumbra.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xande {
    public class PenumbraIPCPathResolver : IPathResolver {
        private readonly DalamudPluginInterface _plugin;
        public PenumbraIPCPathResolver( DalamudPluginInterface pi ) {
            _plugin = pi;
        }

        // TODO: Ability to resolve paths in various manners
        public string ResolveDefaultPath( string gamePath ) {
            var resolvedPath = Ipc.ResolveDefaultPath.Subscriber( _plugin ).Invoke( gamePath );
            PluginLog.Debug( $"Resolving DEFAULT path: \"{gamePath}\" -> \"{resolvedPath}\" " );
            return resolvedPath;
        }

        public string ResolveCharacterPath( string gamePath, string characterName ) {
            var resolvedPath = Ipc.ResolveCharacterPath.Subscriber( _plugin ).Invoke( gamePath, characterName );
            PluginLog.Debug( $"Resolving character \"{characterName}\" : \"{gamePath}\" -> \"{resolvedPath}\"" );
            return resolvedPath;
        }

        public string ResolvePlayerPath( string gamePath ) {
            var resolvedPath = Ipc.ResolvePlayerPath.Subscriber( _plugin ).Invoke( gamePath );
            PluginLog.Debug( $"Resolving PLAYER path: \"{gamePath}\" -> \"{resolvedPath}\"" );
            return resolvedPath;
        }

        public IList<string> GetCollections() {
            return Ipc.GetCollections.Subscriber( _plugin ).Invoke();
        }
    }
}
