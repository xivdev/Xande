using Lumina.Models.Models;
using SharpGLTF.Geometry.VertexTypes;

namespace Xande;

public static class VertexUtility {
    /// <summary> Obtain the correct geometry type for a given set of vertices. </summary>
    public static Type GetVertexGeometryType( Vertex[] vertex )
        => vertex[ 0 ].Tangent1 != null ? typeof( VertexPositionNormalTangent ) :
            vertex[ 0 ].Normal  != null ? typeof( VertexPositionNormal ) : typeof( VertexPosition );

    /// <summary> Obtain the correct material type for a set of vertices. </summary>
    public static Type GetVertexMaterialType( Vertex[] vertex ) {
        var hasColor = vertex[ 0 ].Color != null;
        var hasUv    = vertex[ 0 ].UV    != null;

        return hasColor switch {
            true when hasUv  => typeof( VertexColor1Texture1 ),
            false when hasUv => typeof( VertexTexture1 ),
            _                => typeof( VertexColor1 ),
        };
    }
}