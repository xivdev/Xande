using System.Numerics;
using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace Xande;

public class MeshBuilder {
    private readonly Mesh           _mesh;
    private readonly List< object > _geometryParamCache  = new();
    private readonly List< object > _materialParamCache  = new();
    private readonly List< object > _skinningParamCache  = new();
    private readonly object[]       _vertexBuilderParams = new object[3];

    private readonly Type _geometryT;
    private readonly Type _materialT;
    private readonly Type _skinningT;
    private readonly Type _vertexBuilderT;
    private readonly Type _meshBuilderT;

    public MeshBuilder( Mesh mesh ) {
        _mesh = mesh;

        _geometryT      = GetVertexGeometryType( _mesh.Vertices );
        _materialT      = GetVertexMaterialType( _mesh.Vertices );
        _skinningT      = GetVertexSkinningType( _mesh.Vertices );
        _vertexBuilderT = typeof( VertexBuilder< ,, > ).MakeGenericType( _geometryT, _materialT, _skinningT );
        _meshBuilderT   = typeof( MeshBuilder< ,,, > ).MakeGenericType( typeof( MaterialBuilder ), _geometryT, _materialT, _skinningT );
    }

    public IMeshBuilder< MaterialBuilder > BuildSubmesh( IReadOnlyDictionary< int, int > jointMap, MaterialBuilder materialBuilder, Submesh submesh, int lastOffset ) {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( materialBuilder );

        for( var triIdx = 0; triIdx < submesh.IndexNum; triIdx += 3 ) {
            var triA = BuildVertex( jointMap, triIdx + ( int )submesh.IndexOffset + 0 - lastOffset );
            var triB = BuildVertex( jointMap, triIdx + ( int )submesh.IndexOffset + 1 - lastOffset );
            var triC = BuildVertex( jointMap, triIdx + ( int )submesh.IndexOffset + 2 - lastOffset );
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    public IVertexBuilder BuildVertex( IReadOnlyDictionary< int, int > jointMap, int vertexIdx ) {
        ClearCaches();

        var vertex = _mesh.VertexByIndex( vertexIdx );
        _geometryParamCache.Add( ToVec3( vertex.Position!.Value ) );

        // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
        if( _geometryT != typeof( VertexPosition ) ) _geometryParamCache.Add( vertex.Normal!.Value );

        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        if( _geometryT == typeof( VertexPositionNormalTangent ) ) _geometryParamCache.Add( vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 } );

        // AKA: Has "TextureN" component
        if( _materialT != typeof( VertexColor1 ) ) _materialParamCache.Add( ToVec2( vertex.UV!.Value ) );

        // AKA: Has "Color1" component
        //if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, new Vector4( 255, 255, 255, 255 ) );

        if( _skinningT != typeof( VertexEmpty ) ) {
            for( var k = 0; k < 4; k++ ) {
                var boneIndex       = vertex.BlendIndices[ k ];
                var mappedBoneIndex = jointMap[ boneIndex ];
                var boneWeight      = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;
                var binding         = ( mappedBoneIndex, boneWeight );
                _skinningParamCache.Add( binding );
            }
        }

        _vertexBuilderParams[ 0 ] = Activator.CreateInstance( _geometryT, _geometryParamCache.ToArray() )!;
        _vertexBuilderParams[ 1 ] = Activator.CreateInstance( _materialT, _materialParamCache.ToArray() )!;
        _vertexBuilderParams[ 2 ] = Activator.CreateInstance( _skinningT, _skinningParamCache.ToArray() )!;

        return ( IVertexBuilder )Activator.CreateInstance( _vertexBuilderT, _vertexBuilderParams )!;
    }

    private void ClearCaches() {
        _geometryParamCache.Clear();
        _materialParamCache.Clear();
        _skinningParamCache.Clear();
    }

    /// <summary> Obtain the correct geometry type for a given set of vertices. </summary>
    private static Type GetVertexGeometryType( Vertex[] vertex )
        => vertex[ 0 ].Tangent1 != null ? typeof( VertexPositionNormalTangent ) :
            vertex[ 0 ].Normal != null  ? typeof( VertexPositionNormal ) : typeof( VertexPosition );

    /// <summary> Obtain the correct material type for a set of vertices. </summary>
    private static Type GetVertexMaterialType( Vertex[] vertex ) {
        var hasColor = vertex[ 0 ].Color != null;
        var hasUv    = vertex[ 0 ].UV != null;

        return hasColor switch {
            true when hasUv  => typeof( VertexColor1Texture1 ),
            false when hasUv => typeof( VertexTexture1 ),
            _                => typeof( VertexColor1 ),
        };
    }

    private static Type GetVertexSkinningType( Vertex[] vertex ) {
        var hasBoneWeights = vertex[ 0 ].BlendWeights != null;
        //return hasBoneWeights ? typeof( VertexJoints8 ) : typeof( VertexJoints4 );
        return typeof( VertexJoints4 );
    }

    private static Vector3 ToVec3( Vector4 v ) => new(v.X, v.Y, v.Z);
    private static Vector2 ToVec2( Vector4 v ) => new(v.X, v.Y);
}