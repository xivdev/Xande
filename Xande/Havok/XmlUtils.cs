using System.Globalization;
using System.Text.RegularExpressions;

namespace Xande.Havok;

public static class XmlUtils {
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
}