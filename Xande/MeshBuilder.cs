using System.Numerics;
using Dalamud.Logging;
using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using Xande.Files;

namespace Xande;

public class MeshBuilder {
    private readonly Mesh                 _mesh;
    private readonly List< object >       _geometryParamCache  = new();
    private readonly List< object >       _materialParamCache  = new();
    private readonly List< (int, float) > _skinningParamCache  = new();
    private readonly object[]             _vertexBuilderParams = new object[3];

    private readonly IReadOnlyDictionary< int, int > _jointMap;
    private readonly MaterialBuilder                 _materialBuilder;
    private readonly RaceDeformer                    _raceDeformer;

    private readonly Type _geometryT;
    private readonly Type _materialT;
    private readonly Type _skinningT;
    private readonly Type _vertexBuilderT;
    private readonly Type _meshBuilderT;

    private List< PbdFile.Deformer > _deformers = new();

    public MeshBuilder(
        Mesh mesh,
        bool useSkinning,
        IReadOnlyDictionary< int, int > jointMap,
        MaterialBuilder materialBuilder,
        RaceDeformer raceDeformer
    ) {
        _mesh            = mesh;
        _jointMap        = jointMap;
        _materialBuilder = materialBuilder;
        _raceDeformer    = raceDeformer;

        _geometryT      = GetVertexGeometryType( _mesh.Vertices );
        _materialT      = GetVertexMaterialType( _mesh.Vertices );
        _skinningT      = useSkinning ? typeof( VertexJoints4 ) : typeof( VertexEmpty );
        _vertexBuilderT = typeof( VertexBuilder< ,, > ).MakeGenericType( _geometryT, _materialT, _skinningT );
        _meshBuilderT   = typeof( MeshBuilder< ,,, > ).MakeGenericType( typeof( MaterialBuilder ), _geometryT, _materialT, _skinningT );
    }

    public void SetupDeformSteps( ushort from, ushort to ) {
        var     deformSteps = new List< ushort >();
        ushort? current     = to;

        while( current != null ) {
            deformSteps.Add( current.Value );
            current = _raceDeformer.GetParent( current.Value );
        }

        // Slice to only the steps we need
        var fromIdx = deformSteps.IndexOf( from );
        if( fromIdx == -1 ) {
            // TODO: better exception
            throw new ArgumentException( $"Can't find a path from {from:D4} to {to:D4}" );
        }

        deformSteps = deformSteps.GetRange( 0, fromIdx + 1 );

        // Reverse it & cut off the point we're already at
        deformSteps.Reverse();
        deformSteps = deformSteps.GetRange( 1, deformSteps.Count - 1 );

        // Turn these into deformers
        var pbd       = _raceDeformer.PbdFile;
        var deformers = new PbdFile.Deformer[deformSteps.Count];
        for( var i = 0; i < deformSteps.Count; i++ ) {
            var raceCode = deformSteps[ i ];
            var deformer = pbd.GetDeformerFromRaceCode( raceCode );
            deformers[ i ] = deformer;
        }

        _deformers = deformers.ToList();
    }

    public IMeshBuilder< MaterialBuilder > BuildSubmesh( Submesh submesh, int lastOffset ) {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( _materialBuilder );

        for( var triIdx = 0; triIdx < submesh.IndexNum; triIdx += 3 ) {
            var triA = BuildVertex( triIdx + ( int )submesh.IndexOffset + 0 - lastOffset );
            var triB = BuildVertex( triIdx + ( int )submesh.IndexOffset + 1 - lastOffset );
            var triC = BuildVertex( triIdx + ( int )submesh.IndexOffset + 2 - lastOffset );
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    public IMeshBuilder< MaterialBuilder > BuildMesh( int lastOffset ) {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( _materialBuilder );

        for( var triIdx = 0; triIdx < _mesh.Indices.Length; triIdx += 3 ) {
            var triA = BuildVertex( triIdx + 0 - lastOffset );
            var triB = BuildVertex( triIdx + 1 - lastOffset );
            var triC = BuildVertex( triIdx + 2 - lastOffset );
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    public IVertexBuilder BuildVertex( int vertexIdx ) {
        ClearCaches();

        var     vertex      = _mesh.VertexByIndex( vertexIdx );
        Vector3 origPos     = ToVec3( vertex.Position!.Value );
        Vector3 deformedPos = Vector3.Zero;

        if( _skinningT != typeof( VertexEmpty ) ) {
            for( var k = 0; k < 4; k++ ) {
                var boneIndex       = vertex.BlendIndices[ k ];
                var mappedBoneIndex = _jointMap[ boneIndex ];
                var boneWeight      = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;

                var binding = ( mappedBoneIndex, boneWeight );
                _skinningParamCache.Add( binding );
            }
        }

        foreach( var deformer in _deformers ) {
            foreach( var (idx, weight) in _skinningParamCache ) {
                if( weight == 0 ) continue;

                var deformPos = _raceDeformer.DeformVertex( deformer, idx, origPos );
                if( deformPos != null ) { deformedPos += deformPos.Value * weight; }
            }

            origPos     = deformedPos;
            deformedPos = Vector3.Zero;
        }

        _geometryParamCache.Add( origPos );

        // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
        if( _geometryT != typeof( VertexPosition ) ) _geometryParamCache.Add( vertex.Normal!.Value );

        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        if( _geometryT == typeof( VertexPositionNormalTangent ) ) {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            _geometryParamCache.Add( vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 } );
        }

// AKA: Has "TextureN" component
        if( _materialT != typeof( VertexColor1 ) ) _materialParamCache.Add( ToVec2( vertex.UV!.Value ) );

// AKA: Has "Color1" component
//if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, new Vector4( 255, 255, 255, 255 ) );


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

/*private static Type GetVertexSkinningType( Vertex[] vertex ) {
    var hasBoneWeights = vertex[ 0 ].BlendWeights != null;
    //return hasBoneWeights ? typeof( VertexJoints8 ) : typeof( VertexJoints4 );
    return typeof( VertexJoints4 );
}*/

    private static Vector3 ToVec3( Vector4 v ) => new(v.X, v.Y, v.Z);
    private static Vector2 ToVec2( Vector4 v ) => new(v.X, v.Y);
}