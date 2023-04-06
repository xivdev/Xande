using Dalamud.Logging;
using Lumina.Data;
using Lumina.Extensions;

// ReSharper disable UnassignedField.Global
#pragma warning disable CS8618

namespace Xande.Files;

/// <summary>
/// Parses a human.pbd file to deform models. This file is located at <c>chara/xls/boneDeformer/human.pbd</c> in the game's data files.
/// </summary>
public class PbdFile : FileResource {
    public Header[]   Headers;
    public Deformer[] Deformers;

    public override void LoadFile() {
        var entryCount = Reader.ReadInt32();

        Headers   = new Header[entryCount];
        Deformers = new Deformer[entryCount];

        for( var i = 0; i < entryCount; i++ ) { Headers[ i ] = Reader.ReadStructure< Header >(); }

        // No idea what this is
        var unkSize = entryCount * 8;
        Reader.Seek( Reader.BaseStream.Position + unkSize );

        // First deformer (101) seems... strange, just gonna skip it for now
        for( var i = 1; i < entryCount; i++ ) {
            var header = Headers[ i ];
            var offset = header.Offset;
            Reader.Seek( offset );

            Deformers[ i ] = Deformer.Read( Reader );
        }
    }

    public struct Header {
        public ushort Id;
        public ushort DeformerId;
        public int    Offset;
        public float  Unk2;
    }

    public struct Deformer {
        public int        BoneCount;
        public string[]   BoneNames;
        public float[]?[] DeformMatrices;

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

    public Deformer GetDeformerFromRaceCode( ushort raceCode ) {
        var header = Headers.First( h => h.Id == raceCode );
        return Deformers[ header.DeformerId ];
    }
}