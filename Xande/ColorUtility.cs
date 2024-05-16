using Dalamud.Memory;
using SkiaSharp;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ReSharper disable MemberCanBePrivate.Global

namespace Xande;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct ColorSetRow {
    public fixed ushort DataBuffer[16];

    public Span<Half> Data => new( ( Half* )Unsafe.AsPointer( ref DataBuffer[0] ), 16 );

    public ColorSetRow( ReadOnlySpan<Half> data ) {
        if( data.Length != 16 )
            throw new ArgumentException( "Color set row must be 16 elements long.", nameof( data ) );

        data.CopyTo( Data );
    }

    public Vector3 Diffuse => new( (float)Data[0], ( float )Data[1], ( float )Data[2] );
    public Vector3 Specular => new( ( float )Data[4], ( float )Data[5], ( float )Data[6] );
    public Vector3 Emissive => new( ( float )Data[8], ( float )Data[9], ( float )Data[10] );

    [Obsolete]
    public Vector2 TileRepeat => new( ( float )Data[12], ( float )Data[15] );
    [Obsolete]
    public Vector2 TileSkew => new( ( float )Data[13], ( float )Data[14] );

    public Vector4 TileMatrix => new( ( float )Data[12], ( float )Data[13], ( float )Data[14], ( float )Data[15] );

    public float SpecularStrength => ( float )Data[3];
    public float GlossStrength => ( float )Data[7];

    public ushort TileSet => ( ushort )( ( float )Data[11] * 64 );
}

[StructLayout( LayoutKind.Sequential, Size = 512 )]
public unsafe struct ColorSet {
    public fixed ushort DataBuffer[256];

    public Span<ushort> DataBits => new( Unsafe.AsPointer( ref DataBuffer[0] ), 256 );
    public Span<Half> Data => MemoryMarshal.Cast<ushort, Half>( DataBits );
    public Span<ColorSetRow> Rows => MemoryMarshal.Cast<ushort, ColorSetRow>( DataBits );

    public ColorSet( ReadOnlySpan<Half> data ) {
        if( data.Length != 256 )
            throw new ArgumentException( "Color set must be 256 elements long.", nameof( data ) );

        data.CopyTo( Data );
    }

    public (SKColorF Diffuse, SKColorF Specular, SKColorF Emissive, Vector4 TileMatrix, ushort TileSet, float SpecularStrength, float GlossStrength) Blend( byte alpha ) {
        var (row0, row1, blend) = GetBlendedRow( alpha );
        var tileRow = Rows[(int)GetDiscreteIdx( alpha )];

        var diffuse = Vector3.Lerp( row0.Diffuse, row1.Diffuse, blend );
        var specular = Vector3.Lerp( row0.Specular, row1.Specular, blend );
        var emissive = Vector3.Lerp( row0.Emissive, row1.Emissive, blend );
        var tileMatrix = Vector4.Lerp( row0.TileMatrix, row1.TileMatrix, blend );
        var specularStrength = ColorUtility.Lerp( row0.SpecularStrength, row1.SpecularStrength, blend );
        var glossStrength = ColorUtility.Lerp( row0.GlossStrength, row1.GlossStrength, blend );
        
        return (diffuse.AsColorF(), specular.AsColorF(), emissive.AsColorF(), tileMatrix, tileRow.TileSet, specularStrength, glossStrength);
    }

    public (ColorSetRow A, ColorSetRow B, float Blend) GetBlendedRow( byte alpha ) {
        var blendIdx = GetBlendedIdx( alpha );
        var idx0 = MathF.Floor( blendIdx );
        var blend = blendIdx - idx0;
        var idx1 = MathF.Min( idx0 + 1, 15 );

        var row0 = Rows[( int )idx0];
        var row1 = Rows[( int )idx1];

        return (row0, row1, blend);
    }

    public static float GetBlendedIdx( byte alpha ) {
        var r0y = alpha / 255f;
        var r7x = 15 * r0y;
        var r7y = 7.5f * r0y;
        var r0z = r7y % 1f;
        r0z = r0z + r0z;
        var r2w = r0y * 15 + 0.5f;
        r0z = MathF.Floor( r0z );
        r2w = MathF.Floor( r2w );
        r0y = -r0y * 15 + r2w;
        r0y = r0z * r0y + r7x;

        return r0y;
    }

    public static float GetDiscreteIdx( byte alpha ) {
        var r0y = GetBlendedIdx( alpha );

        r0y = 0.5f + r0y;
        r0y = MathF.Floor( r0y );

        return r0y;
    }
}

public static class ColorUtility {
    public enum TextureType {
        Diffuse  = 0,
        Specular = 4,
        Emissive = 8,
    };

    public static SKColorF AsColorF( this Vector3 vec ) => new( vec.X, vec.Y, vec.Z );

    public static SKColorF AsColorF( this Vector4 vec ) => new( vec.X, vec.Y, vec.Z, vec.W );

    public static Vector4 AsVector4( this SKColorF colorF ) => new( colorF.Red, colorF.Green, colorF.Blue, colorF.Alpha );

    public static Vector2 AsVector2_XY(this Vector4 vec) => new(vec.X, vec.Y);

    public static Vector2 AsVector2_ZW(this Vector4 vec) => new(vec.Z, vec.W);

    public static Vector3 Sign(this Vector3 vec) => new(MathF.Sign(vec.X), MathF.Sign(vec.Y), MathF.Sign(vec.Z));

    public static SKColor AsColor( this SKColorF colorF ) => (SKColor)colorF;

    public static SKColorF BilinearSample(this SKBitmap bitmap, Vector2 uv ) {
        var u = uv.X * bitmap.Width;
        var v = uv.Y * bitmap.Height;

        var x0 = ( int )MathF.Floor( u );
        var y0 = ( int )MathF.Floor( v );

        var x1 = ( int )MathF.Ceiling( u );
        var y1 = ( int )MathF.Ceiling( v );

        x1 = Math.Min( x1, bitmap.Width - 1 );
        y1 = Math.Min( y1, bitmap.Height - 1 );

        if ( x0 == x1 && y0 == y1 )
            return bitmap.GetPixel( x0, y0 );
        else if (x0 == x1)
            return Lerp( bitmap.GetPixel( x0, y0 ), bitmap.GetPixel( x0, y1 ), v - y0 );
        else if (y0 == y1)
            return Lerp( bitmap.GetPixel( x0, y0 ), bitmap.GetPixel( x1, y0 ), u - x0 );
        else
            return Lerp(
                        Lerp( bitmap.GetPixel( x0, y0 ), bitmap.GetPixel( x1, y0 ), u - x0 ),
                        Lerp( bitmap.GetPixel( x0, y1 ), bitmap.GetPixel( x1, y1 ), u - x0 ),
                    v - y0 );
    }

    public static SKColorF Lerp(SKColorF color, SKColorF other, float blend ) =>
        Vector4.Lerp(color.AsVector4(), other.AsVector4(), blend).AsColorF();

    public static float Lerp( float a, float b, float blend ) =>
        ( a * ( 1.0f - blend ) ) + ( b * blend );

    public static Vector2 TransformUV( Vector2 uv, Vector4 matrix ) {
        var x = Vector2.Dot( uv, new( matrix.X, matrix.Y ) );
        var y = Vector2.Dot( uv, new( matrix.Z, matrix.W ) );

        return new( x, y );
    }

    // source: character.shpk ps10
    public static SKColorF MixNormals(SKColorF normalColor, SKColorF tileColor) {
        var normal = normalColor.AsVector4();
        var tileNormal = tileColor.AsVector4();

        var r1 = new Vector3( tileNormal.X, tileNormal.Y, 0 );
        r1 -= new Vector3( 0.5f, 0.5f, 0 );
        var r0w = r1.LengthSquared();
        r0w = .25f - r0w;
        r0w = MathF.Max( 0, r0w );
        r1.Z = MathF.Sqrt( r0w );
        r1 = Vector3.Normalize( r1 );

        var r2 = new Vector3( normal.X, normal.Y, 0 );

        var r0x = normal.Z; // * vertex color alpha
        r0x = .25f - r0x;
        r0x = MathF.Max( 0, r0x );
        r2.Z = MathF.Sqrt( r0x );

        var r0 = Vector3.Normalize(r2);

        static float Sign(float v) => 0 < v ? 1 : -1;

        r2 = new( Sign( r0.X ), Sign( r0.Y ), Sign( r0.Z ) );
        var r3 = Vector3.One - r2;
        r0 = Vector3.One - Vector3.Abs( r0 );
        r1 = r1 + r2;
        r0 = r0 * r1 + r3;
        r0 = Vector3.Normalize( r0 );

        return ((r0 + Vector3.One) * .5f).AsColorF();
    }

    public static Vector3 Lerp( Vector3 x, Vector3 y, Vector3 s ) =>
        x * ( Vector3.One - s ) + y * s;
}