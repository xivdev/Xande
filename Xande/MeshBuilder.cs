using System.Numerics;
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

    private readonly List< IVertexBuilder > _vertices;

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
        _vertices       = _mesh.Vertices.Select( BuildVertex ).ToList();
    }

    public void SetupDeformSteps( ushort from, ushort to ) {
        // Nothing to do
        if( from == to ) return;

        var     deformSteps = new List< ushort >();
        ushort? current     = to;

        while( current != null ) {
            deformSteps.Add( current.Value );
            current = _raceDeformer.GetParent( current.Value );
            if( current == from ) break;
        }

        // Reverse it to the right order
        deformSteps.Reverse();

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
            var triA = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 0 - lastOffset ] ];
            var triB = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 1 - lastOffset ] ];
            var triC = _vertices[ _mesh.Indices[ triIdx + ( int )submesh.IndexOffset + 2 - lastOffset ] ];
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    public IMeshBuilder< MaterialBuilder > BuildMesh( int lastOffset ) {
        var ret       = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( _meshBuilderT, string.Empty )!;
        var primitive = ret.UsePrimitive( _materialBuilder );

        for( var triIdx = 0; triIdx < _mesh.Indices.Length; triIdx += 3 ) {
            var triA = _vertices[ _mesh.Indices[ triIdx + 0 - lastOffset ] ];
            var triB = _vertices[ _mesh.Indices[ triIdx + 1 - lastOffset ] ];
            var triC = _vertices[ _mesh.Indices[ triIdx + 2 - lastOffset ] ];
            primitive.AddTriangle( triA, triB, triC );
        }

        return ret;
    }

    public void BuildShapes( IReadOnlyList< Shape > shapes, IMeshBuilder< MaterialBuilder > builder, int offset ) {
        var primitive = builder.Primitives.First();
        var triangles = primitive.Triangles;
        var vertices  = primitive.Vertices;
        for( var i = 0; i < shapes.Count; ++i ) {
            var shape = shapes[ i ];
            var morph = builder.UseMorphTarget( i );
            foreach( var shapeMesh in shape.Meshes.Where( m => m.MeshIndex == _mesh.MeshIndex ) ) {
                foreach( var (baseTri, otherTri) in shapeMesh.Values ) {
                    var triIdx    = baseTri - offset;
                    var vertexIdx = triIdx % 3;
                    triIdx /= 3;
                    if( triIdx < 0 || triIdx >= triangles.Count ) continue; // different submesh.

                    var triA = triangles[ triIdx ];
                    var vertexA = vertices[ vertexIdx switch {
                        0 => triA.A,
                        1 => triA.B,
                        _ => triA.C,
                    } ];

                    morph.SetVertex( vertexA.GetGeometry(), _vertices[ otherTri ].GetGeometry() );
                }
            }
        }

        var data = new ExtraDataManager();
        data.AddShapeNames( shapes );
        builder.Extras = data.Serialize();
    }

    private IVertexBuilder BuildVertex( Vertex vertex ) {
        ClearCaches();

        if( _skinningT != typeof( VertexEmpty ) ) {
            for( var k = 0; k < 4; k++ ) {
                var boneIndex       = vertex.BlendIndices[ k ];
                var mappedBoneIndex = _jointMap[ boneIndex ];
                var boneWeight      = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;

                var binding = ( mappedBoneIndex, boneWeight );
                _skinningParamCache.Add( binding );
            }
        }

        var origPos    = ToVec3( vertex.Position!.Value );
        var currentPos = origPos;

        if( _deformers.Count > 0 ) {
            foreach( var deformer in _deformers ) {
                var deformedPos = Vector3.Zero;

                foreach( var (idx, weight) in _skinningParamCache ) {
                    if( weight == 0 ) continue;

                    var deformPos                       = _raceDeformer.DeformVertex( deformer, idx, currentPos );
                    if( deformPos != null ) deformedPos += deformPos.Value * weight;
                }

                currentPos = deformedPos;
            }
        }

        _geometryParamCache.Add( currentPos );

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
            vertex[ 0 ].Normal  != null ? typeof( VertexPositionNormal ) : typeof( VertexPosition );

    /// <summary> Obtain the correct material type for a set of vertices. </summary>
    private static Type GetVertexMaterialType( Vertex[] vertex ) {
        var hasColor = vertex[ 0 ].Color != null;
        var hasUv    = vertex[ 0 ].UV    != null;

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