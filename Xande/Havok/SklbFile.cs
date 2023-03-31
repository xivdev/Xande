namespace Xande.Havok;

/// <summary>
/// Class for parsing .hkx data from a .sklb file.
/// </summary>
public sealed class SklbFile : BinaryReader {
    public readonly  int    HkxOffset;
    private readonly byte[] _header;
    public byte[] HkxData { get; private set; }

    /// <summary>
    /// Constructs a new SklbFile instance from a stream.
    /// </summary>
    /// <param name="stream">A stream to the .sklb data.</param>
    /// <exception cref="Exceptions.SklbInvalidException">Thrown if magic does not match.</exception>
    public SklbFile( Stream stream ) : base( stream ) {
        var magic = ReadInt32();
        if( magic != 0x736B6C62 ) {
            throw new Exceptions.SklbInvalidException();
        }

        var version = ReadInt32();

        if( version != 0x31333030 ) {
            // Version one
            ReadInt16(); // Skip unkOffset
            HkxOffset = ReadInt16();
        }
        else {
            ReadInt32(); // Skip unkOffset
            HkxOffset = ReadInt32();
        }

        BaseStream.Seek( 0, SeekOrigin.Begin );
        _header = ReadBytes( HkxOffset );
        BaseStream.Seek( HkxOffset, SeekOrigin.Begin );
        HkxData = ReadBytes( ( int )( BaseStream.Length - HkxOffset ) );
    }

    public void ReplaceHkxData( byte[] hkxData ) {
        HkxData = hkxData;
    }

    public void Write( Stream stream ) {
        stream.Write( _header );
        stream.Write( HkxData );
    }
}