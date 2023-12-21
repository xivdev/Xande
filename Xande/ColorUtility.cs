using SkiaSharp;
using System.Numerics;

// ReSharper disable MemberCanBePrivate.Global

namespace Xande;

public static class ColorUtility {
    public enum TextureType {
        Diffuse  = 0,
        Specular = 4,
        Emissive = 8,
    };

    private static TSelf Lerp<TSelf>( TSelf value1, TSelf value2, TSelf amount ) where TSelf : IFloatingPointIeee754<TSelf> => ( value1 * ( TSelf.One - amount ) ) + ( value2 * amount );

    /// <summary> Blend two color byte values linearly according to a scalar. </summary>
    public static byte Blend( Half c1, Half c2, double blendScalar ) =>
        ( byte )Math.Clamp( Convert.ToInt32( Lerp( ( double )c1, ( double )c2, ( double )blendScalar ) * 256 ), 0, byte.MaxValue );

    /// <summary> Blend a full color according to a scalar, ignoring alpha. </summary>
    public static SKColor Blend( ReadOnlySpan<Half> a, ReadOnlySpan<Half> b, double blendScalar )
        => new(
            Blend( a[0], b[0], blendScalar ),
            Blend( a[1], b[1], blendScalar ),
            Blend( a[2], b[2], blendScalar )
        );

    public static SKColor BlendColorSet( ReadOnlySpan<Half> info, int colorSetIndex1, int colorSetIndex2, byte alpha, double scalar, TextureType type ) {
        if (info.Length != 256)
            throw new ArgumentException($"Color set must be 256 elements long. ({info.Length})", nameof(info));
        return Blend(
            info.Slice( colorSetIndex1 + ( int )type, 3 ),
            info.Slice( colorSetIndex2 + ( int )type, 3 ),
            scalar
        ).WithAlpha(alpha);
    }
}