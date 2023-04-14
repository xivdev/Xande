using SharpGLTF.Schema2;

namespace Xande.Models.Import;

public class StringTableBuilder {
    public readonly List< string > Bones = new();

    public StringTableBuilder( ModelRoot root ) {
        // TODO: is this a stable assumption to make?
        var skelly = root.DefaultScene.FindNode( x => x.Name == "n_root" );
        RecursiveSkeleton( skelly );

        Bones = Bones.Distinct().ToList();
    }

    private void RecursiveSkeleton( Node? node ) {
        if( node is null ) return;
        Bones.Add( node.Name );
        foreach( var child in node.VisualChildren ) { RecursiveSkeleton( child ); }
    }
}