namespace Xande {
    public interface IPathResolver {
        string ResolveDefaultPath( string gamePath );
        string ResolvePlayerPath( string gamePath );
        string ResolveCharacterPath( string gamePath, string characterPath );
    }
}
