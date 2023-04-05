using Lumina.Extensions;

// ReSharper disable NotAccessedField.Global
// ReSharper disable MemberCanBePrivate.Global
#pragma warning disable CS8618

namespace Xande.Files;

/// <summary>
/// Class for parsing .hkx data from a .sklb file.
/// </summary>
public sealed class SklbFile {
    public short VersionOne;
    public short VersionTwo;
    public int   HavokOffset;

    public byte[] RawHeader;
    public byte[] HkxData;

    /// <summary>
    /// Constructs a new SklbFile instance from a stream.
    /// </summary>
    /// <param name="stream">A stream to the .sklb data.</param>
    /// <exception cref="InvalidDataException">Thrown if magic does not match.</exception>
    public static SklbFile FromStream( Stream stream ) {
        using var reader = new BinaryReader( stream );

        var magic = reader.ReadInt32();
        if( magic != 0x736B6C62 ) { throw new InvalidDataException( "Invalid .sklb magic" ); }

        var versionOne = reader.ReadInt16();
        var versionTwo = reader.ReadInt16();

        var isOldHeader = versionTwo switch {
            0x3132 => true,
            0x3133 => false,
            _      => false
        };

        int havokOffset;
        if( isOldHeader ) {
            // Version one
            reader.ReadInt16(); // Skip unkOffset
            havokOffset = reader.ReadInt16();
        }
        else {
            // Version two
            reader.ReadInt32(); // Skip unkOffset
            havokOffset = reader.ReadInt32();
        }

        reader.Seek( 0 );
        var rawHeader = reader.ReadBytes( havokOffset );
        reader.Seek( havokOffset );
        var hkxData = reader.ReadBytes( ( int )( reader.BaseStream.Length - havokOffset ) );

        return new SklbFile {
            VersionOne  = versionOne,
            VersionTwo  = versionTwo,
            HavokOffset = havokOffset,

            RawHeader = rawHeader,
            HkxData   = hkxData
        };
    }

    /// <summary>
    /// Splices the given .hkx file into the .sklb.
    /// </summary>
    /// <param name="hkxData">A byte array representing an .hkx file.</param>
    public void ReplaceHkxData( byte[] hkxData ) {
        HkxData = hkxData;
    }

    public void Write( Stream stream ) {
        stream.Write( RawHeader );
        stream.Write( HkxData );
    }
}