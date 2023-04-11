using SharpGLTF.Schema2;

namespace Xande;

// https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs
// https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Files/MdlFile.cs
public class ModelWriter {
    private ModelRoot    _root;
    private BinaryWriter _w;

    public ModelWriter( ModelRoot root, BinaryWriter w ) {
        _root = root;
        _w    = w;
    }

    private void WriteFileHeader() {
        _w.Write( 0x05000001u ); // Version (picked from a random .mdl, TODO investigate differences)
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

        _w.Write( ( byte )3 ); // LOD
        _w.Write( ( byte )0 ); // Enable index buffer streaming
        _w.Write( ( byte )0 ); // Enable edge geometry
        _w.Write( ( byte )0 ); // Padding
    }

    public void WriteAll() {
        // Skip the header - we'll write it later
        _w.Seek( 0x44, SeekOrigin.Begin );

        // TODO vertex declarations
        // TODO strings
        // TODO model header (diff than file header)
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
}