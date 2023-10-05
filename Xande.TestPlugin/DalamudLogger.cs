using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Lumina;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xande.TestPlugin;

namespace Xande {
    internal class DalamudLogger : ILogger {
        public void Debug( string template, params object[] values ) {
            Plugin.Logger.Debug( template, values );
        }

        public void Error( string template, params object[] values ) {
            Plugin.Logger.Error( template, values );
        }

        public void Fatal( string template, params object[] values ) {
            Plugin.Logger.Fatal( template, values );
        }

        public void Information( string template, params object[] values ) {
            Plugin.Logger.Information( template, values );
        }

        public void Verbose( string template, params object[] values ) {
            Plugin.Logger.Verbose( template, values );
        }

        public void Warning( string template, params object[] values ) {
            Plugin.Logger.Warning( template, values );
        }
    }
}
