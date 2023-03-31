namespace Xande.Havok;

/// <summary>
/// Class for parsing .hkx data from a .sklb file.
/// </summary>
public sealed class SklbFile {
    public readonly  int    HkxOffset;
    private readonly byte[] _header;
    public byte[] HkxData { get; private set; }

    /// <summary>
    /// Constructs a new SklbFile instance from two byte arrays.
    /// </summary>
    /// <param name="header">All content before the Havok data.</param>
    /// <param name="hkxData">The Havok data.</param>
    /// <param name="hkxOffset">The offset in the file at which the Havok data starts.</param>
    private SklbFile( byte[] header, byte[] hkxData, int hkxOffset ) {
        _header   = header;
        HkxData   = hkxData;
        HkxOffset = hkxOffset;
    }

    /// <summary>
    /// Constructs a new SklbFile instance from a stream.
    /// </summary>
    /// <param name="stream">A stream to the .sklb data.</param>
    /// <exception cref="InvalidDataException">Thrown if magic does not match.</exception>
    public static SklbFile FromStream( Stream stream ) {
        using var reader = new BinaryReader( stream );

        var magic = reader.ReadInt32();
        if( magic != 0x736B6C62 ) {
            throw new InvalidDataException( "Invalid .sklb magic" );
        }

        var version = reader.ReadInt32();

        int hkxOffset;
        if( version != 0x31333030 ) {
            // Version one
            reader.ReadInt16(); // Skip unkOffset
            hkxOffset = reader.ReadInt16();
        }
        else {
            // Version two
            reader.ReadInt32(); // Skip unkOffset
            hkxOffset = reader.ReadInt32();
        }

        reader.BaseStream.Seek( 0, SeekOrigin.Begin );
        var header = reader.ReadBytes( hkxOffset );
        reader.BaseStream.Seek( hkxOffset, SeekOrigin.Begin );
        var hkxData = reader.ReadBytes( ( int )( reader.BaseStream.Length - hkxOffset ) );

        return new SklbFile( header, hkxData, hkxOffset );
    }


    public void ReplaceHkxData( byte[] hkxData ) {
        HkxData = hkxData;
    }

    public void Write( Stream stream ) {
        stream.Write( _header );
        stream.Write( HkxData );
    }
}