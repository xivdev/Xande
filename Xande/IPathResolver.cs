using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xande {
    public interface IPathResolver {
        string ResolveDefaultPath( string gamePath );
        string ResolvePlayerPath( string gamePath );
        string ResolveCharacterPath( string gamePath, string characterPath );
    }
}
