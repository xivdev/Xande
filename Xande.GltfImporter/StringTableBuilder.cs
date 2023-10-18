using Lumina;
using SharpGLTF.Schema2;
using System.Text;

namespace Xande.GltfImporter;

public class StringTableBuilder {
    public SortedSet<string> Attributes = new();
    public SortedSet<string> Bones = new();
    public List<string> HierarchyBones = new();
    public readonly List<string> Materials = new();
    public SortedSet<string> Shapes = new();
    public readonly List<string> Extras = new();
    private ILogger? _logger;

    public StringTableBuilder(ILogger? logger = null) {
        _logger = logger;
    }

    public void AddAttribute( string attr ) {
        if( !Attributes.Contains( attr ) ) {
            Attributes.Add( attr );
        }
    }

    public void AddAttributes( IEnumerable<string> attr ) {
        foreach( var a in attr ) {
            AddAttribute( a );
        }
    }

    public bool RemoveAttribute( string attr ) {
        return Attributes.Remove( attr );
    }

    public void AddBones( IEnumerable<string> bones ) {
        foreach( var bone in bones ) {
            AddBone( bone );
        }
    }

    public void AddBone( string bone ) {
        if( !Bones.Contains( bone ) ) {
            Bones.Add( bone );
        }
    }

    public void AddMaterial( string material ) {
        if( !Materials.Contains( material ) ) {
            Materials.Add( material );
        }
    }

    public void AddShape( string shape ) {
        if( !Shapes.Contains( shape ) ) {
            Shapes.Add( shape );
        }
    }

    public void AddShapes( List<string> shapes ) {
        foreach( var s in shapes ) {
            AddShape( s );
        }
    }
    public bool RemoveShape( string shape ) {
        return Shapes.Remove( shape );
    }

    internal int GetStringCount() {
        return Attributes.Count + Bones.Count + Materials.Count + Shapes.Count + Extras.Count;
    }

    internal List<char> GetChars() {
        var str = String.Join( ' ', Attributes, Bones, Materials, Shapes, Extras );
        _logger?.Debug( $"Getting chars: {str}" );
        return str.ToCharArray().ToList();
    }

    internal byte[] GetBytes() {
        var aggregator = GetStrings();

        var str = String.Join( "\0", aggregator );

        // I don't know if this is actually necessary
        if( Attributes.Count == 0 ) {
            str += "\0";
        }
        if( Bones.Count == 0 ) {
            str += "\0";
        }
        if( Materials.Count == 0 ) {
            str += "\0";
        }
        if( Shapes.Count == 0 ) {
            str += "\0";
        }
        if( Extras.Count == 0 ) {
            str += "\0";
        }

        // This one is required, though
        if( !str.EndsWith( "\0" ) ) {
            str += "\0";
        }

        return Encoding.UTF8.GetBytes( str );
    }

    internal uint[] GetAttributeNameOffsets() {
        return GetOffsets( Attributes.ToList() ).ToArray();
    }

    internal uint[] GetMaterialNameOffsets() {
        return GetOffsets( Materials.ToList() ).ToArray();
    }

    internal uint[] GetBoneNameOffsets() {
        return GetOffsets( Bones.ToList() ).ToArray();
    }

    internal uint GetShapeNameOffset( string v ) {
        return GetOffsets( new List<string> { v } ).ToArray()[0];
    }

    public uint GetOffset( string input ) {
        var aggregator = GetStrings();
        var str = string.Join( "\0", aggregator );
        return ( uint )str.IndexOf( input );
    }

    public List<uint> GetOffsets( List<string> strings ) {
        var ret = new List<uint>();
        var aggregator = GetStrings();
        var str = string.Join( "\0", aggregator );
        foreach( var s in strings ) {
            var index = str.IndexOf( s );
            if( index >= 0 ) {
                ret.Add( ( uint )index );
            }
            else {
                _logger?.Error( $"Could not locate index for {s}" );
            }
        }
        return ret;
    }

    internal List<string> GetStrings() {
        var aggregator = new List<string>();
        aggregator.AddRange( Attributes );
        aggregator.AddRange( Bones );
        aggregator.AddRange( Materials );
        aggregator.AddRange( Shapes );
        aggregator.AddRange( Extras );
        return aggregator;
    }
}