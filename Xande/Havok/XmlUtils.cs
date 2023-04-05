using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SharpGLTF.Transforms;

namespace Xande.Havok;

public static class XmlUtils {
    /// <summary>
    /// Parses a vec12 from Havok XML.
    /// </summary>
    /// <param name="innerText">The inner text of the vec12 node.</param>
    /// <returns>An array of floats.</returns>
    public static float[] ParseVec12( string innerText ) {
        var commentRegex = new Regex( "<!--.*?-->" );
        var noComments   = commentRegex.Replace( innerText, "" );

        var floats = noComments.Split( " " )
            .Select( x => x.Trim() )
            .Where( x => !string.IsNullOrWhiteSpace( x ) )
            .Select( x => x[ 1.. ] )
            .Select( x => BitConverter.ToSingle( BitConverter.GetBytes( int.Parse( x, NumberStyles.HexNumber ) ) ) );

        return floats.ToArray();
    }

    /// <summary>
    /// Creates an affine transform for a bone from the reference pose in the Havok XML file.
    /// </summary>
    /// <param name="refPos">The reference pose.</param>
    /// <returns>The affine transform.</returns>
    /// <exception cref="Exception">Thrown if the reference pose is invalid.</exception>
    public static AffineTransform CreateAffineTransform( ReadOnlySpan< float > refPos ) {
        // Compared with packfile vs tagfile and xivModdingFramework code
        if( refPos.Length < 11 ) throw new Exception( "RefPos does not contain enough values for affine transformation." );
        var translation = new Vector3( refPos[ 0 ], refPos[ 1 ], refPos[ 2 ] );
        var rotation    = new Quaternion( refPos[ 4 ], refPos[ 5 ], refPos[ 6 ], refPos[ 7 ] );
        var scale       = new Vector3( refPos[ 8 ], refPos[ 9 ], refPos[ 10 ] );
        return new AffineTransform( scale, rotation, translation );
    }
}