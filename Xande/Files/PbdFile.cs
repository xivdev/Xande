using Lumina.Extensions;

namespace Xande;

/// <summary>
/// Parses a human.pbd file to deform models. This file is located at <c>chara/xls/boneDeformer/human.pbd</c> in the game's data files.
/// </summary>
public class PbdFile {
    public readonly Header[]   Headers;
    public readonly Deformer[] Deformers;

    private PbdFile( Header[] headers, Deformer[] deformers ) {
        Headers   = headers;
        Deformers = deformers;
    }

    /// <summary>
    /// Constructs a new PbdFile instance from a stream.
    /// </summary>
    /// <param name="stream">A stream to a human.pbd file.</param>
    public static PbdFile FromStream( Stream stream ) {
        using var reader = new BinaryReader( stream );

        var entryCount = reader.ReadInt32();

        var headers   = new Header[entryCount];
        var deformers = new Deformer[entryCount - 1];

        for( var i = 0; i < entryCount; i++ ) { headers[ i ] = reader.ReadStructure< Header >(); }

        // No idea what this is
        var unkSize = entryCount * 8;
        reader.Seek( reader.BaseStream.Position + unkSize );

        // First deformer (101) seems... strange, just gonna skip it for now
        for( var i = 1; i < entryCount; i++ ) {
            var header = headers[ i ];
            var offset = header.Offset;
            reader.Seek( offset );

            deformers[ i - 1 ] = Deformer.Read( reader );
        }

        return new PbdFile( headers, deformers );
    }

    public struct Header {
        public ushort Id;
        public ushort Unk1;
        public int    Offset;
        public float  Unk2;
    }

    public struct Deformer {
        public int       BoneCount;
        public string[]  BoneNames;
        public float[][] DeformMatrices;

        public static Deformer Read( BinaryReader reader ) {
            var boneCount      = reader.ReadInt32();
            var offsetStartPos = reader.BaseStream.Position;

            var boneNames = new string[boneCount];
            var offsets   = reader.ReadStructuresAsArray< short >( boneCount );

            // Read bone names
            for( var i = 0; i < boneCount; i++ ) {
                var offset = offsets[ i ];
                reader.Seek( offsetStartPos - 4 + offset );

                // is there really no better way to read a null terminated string?
                var str = "";

                while( true ) {
                    var c = reader.ReadChar();
                    if( c == '\0' ) { break; }

                    str += c;
                }

                boneNames[ i ] = str;
            }

            // ???
            var offsetEndPos = reader.BaseStream.Position;
            var padding      = boneCount * 2 % 4;
            reader.Seek( offsetStartPos + boneCount * 2 + padding );

            var deformMatrices = new float[boneCount][];
            for( var i = 0; i < boneCount; i++ ) {
                var deformMatrix = reader.ReadStructuresAsArray< float >( 12 );
                deformMatrices[ i ] = deformMatrix;
            }

            reader.Seek( offsetEndPos );

            return new Deformer {
                BoneCount      = boneCount,
                BoneNames      = boneNames,
                DeformMatrices = deformMatrices
            };
        }
    }
}