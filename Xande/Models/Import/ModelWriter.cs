using Lumina.Models.Models;
using SharpGLTF.Schema2;
using Mesh = SharpGLTF.Schema2.Mesh;

namespace Xande.Models.Import;

// https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs
// https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Files/MdlFile.cs
public class ModelWriter : IDisposable {
    private ModelRoot _root;
    private Model     _origModel;

    private BinaryWriter _w;

    private Dictionary< int, Dictionary< int, Mesh > > _meshes = new();

    public ModelWriter( ModelRoot root, Model model, Stream stream ) {
        _root      = root;
        _origModel = model;

        _w = new BinaryWriter( stream );

        foreach( var mesh in root.LogicalMeshes ) {
            var name = mesh.Name;
            var idx  = name.LastIndexOf( '_' );
            var str  = name.Substring( idx + 1 );

            var isSubmesh = str.Contains( '.' );

            if( isSubmesh ) {
                var parts      = str.Split( '.' );
                var meshIdx    = int.Parse( parts[ 0 ] );
                var submeshIdx = int.Parse( parts[ 1 ] );

                if( !_meshes.ContainsKey( meshIdx ) ) _meshes[ meshIdx ] = new();
                _meshes[ meshIdx ][ submeshIdx ] = mesh;
            } else {
                var meshIdx = int.Parse( str );

                if( !_meshes.ContainsKey( meshIdx ) ) _meshes[ meshIdx ] = new();
                _meshes[ meshIdx ][ -1 ] = mesh;
            }
        }
    }

    private void WriteFileHeader() {
        var origHeader = _origModel.File!.FileHeader;

        _w.Write( origHeader.Version );
        _w.Write( ( uint )0 );   // Stack size TODO
        _w.Write( ( uint )0 );   // Runtime size TODO
        _w.Write( ( ushort )0 ); // Vertex declaration len TODO
        _w.Write( ( ushort )0 ); // Material len TODO

        // Vertex offsets TODO
        for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }

        // Index offsets TODO
        for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }

        // Vertex buffer size TODO
        for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }

        // Index buffer size TODO
        for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }

        _w.Write( ( byte )3 );                             // LOD TODO
        _w.Write( origHeader.EnableIndexBufferStreaming ); // Enable index buffer streaming
        _w.Write( origHeader.EnableEdgeGeometry );         // Enable edge geometry
        _w.Write( ( byte )0 );                             // Padding
    }

    private void WriteModelHeader() {
        var origHeader = _origModel.File!.ModelHeader;

        _w.Write( origHeader.Radius );
        _w.Write( ( ushort )_meshes.Count );
        _w.Write( origHeader.AttributeCount );
        _w.Write( ( ushort )_meshes.Sum( x => x.Value.Count( y => y.Key != -1 ) ) );
        _w.Write( origHeader.MaterialCount );
        _w.Write( origHeader.BoneCount );
        _w.Write( origHeader.BoneTableCount );
        _w.Write( origHeader.ShapeCount );
        _w.Write( origHeader.ShapeMeshCount );
        _w.Write( origHeader.ShapeValueCount );
        _w.Write( origHeader.LodCount );

        // Flags are private, so we need to do this - ugly
        _w.Write( ( byte )( origHeader.DustOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.SnowOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.RainOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.Unknown1 ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.BgLightingReflectionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.WavingAnimationDisabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.LightShadowDisabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ShadowDisabled ? 1 : 0 ) );

        _w.Write( origHeader.ElementIdCount );
        _w.Write( origHeader.TerrainShadowMeshCount );

        _w.Write( ( byte )( origHeader.Unknown2 ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.BgUvScrollEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.EnableForceNonResident ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ExtraLodEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ShadowMaskEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ForceLodRangeEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.EdgeGeometryEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.Unknown3 ? 1 : 0 ) );

        _w.Write( origHeader.ModelClipOutDistance );
        _w.Write( origHeader.ShadowClipOutDistance );
        _w.Write( origHeader.Unknown4 );
        _w.Write( origHeader.TerrainShadowSubmeshCount );
        _w.Write( ( byte )0 ); // ??? why is that private in lumina
        _w.Write( origHeader.BGChangeMaterialIndex );
        _w.Write( origHeader.BGCrestChangeMaterialIndex );
        _w.Write( origHeader.Unknown6 );
        _w.Write( origHeader.Unknown7 );
        _w.Write( origHeader.Unknown8 );
        _w.Write( origHeader.Unknown9 );

        _w.Seek( 6, SeekOrigin.Current );
    }

    public void WriteAll() {
        // Skip the header - we'll write it later
        _w.Seek( 0x44, SeekOrigin.Begin );

        // TODO vertex declarations
        // TODO strings
        WriteModelHeader();
        // TODO eids

        // TODO lods
        // TODO extra lods (or not)

        // TODO meshes
        // TODO attributes
        // TODO terrain shadow meshes
        // TODO submeshes
        // TODO terrain shadow submeshes
        // TODO materials

        // TODO bones
        // TODO bone tables

        // TODO shapes
        // TODO shape meshes
        // TODO shape values

        // TODO submesh bone map
        // TODO padding

        // TODO bounding boxes
        // TODO model bounding boxes
        // TODO water bounding boxes
        // TODO fog bounding boxes
        // TODO bone bounding boxes

        // TODO vertex data (I think)

        // Now write the header, now that everything's in place
        _w.Seek( 0, SeekOrigin.Begin );
        WriteFileHeader();
    }

    public void Dispose() {
        _w.Dispose();
    }
}