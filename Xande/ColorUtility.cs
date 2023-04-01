using System.Drawing;

namespace Xande;

public static class ColorUtility {


    /// <summary> Convert a half-width float given as ushort to a byte value. </summary>
    public static byte HalfToByte( ushort s ) => ( byte )Math.Clamp( Convert.ToInt32( ( float )BitConverter.UInt16BitsToHalf( s ) * 256 ), 0, byte.MaxValue );

    /// <summary> Blend two color byte values linearly according to a scalar. </summary>
    public static byte Blend( byte c1, byte c2, double blendScalar ) => ( byte )Math.Clamp( Convert.ToInt32( c2 * blendScalar + c1 * ( 1 - blendScalar ) ), 0, byte.MaxValue );

    /// <summary> Blend a full color according to a scalar, ignoring alpha. </summary>
    public static Color Blend( byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, byte alpha, double blendScalar )
        => Color.FromArgb(
            alpha,
            Blend( r1, r2, blendScalar ),
            Blend( g1, g2, blendScalar ),
            Blend( b1, b2, blendScalar )
        );
}