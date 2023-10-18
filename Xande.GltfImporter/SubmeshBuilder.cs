using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using Microsoft.VisualBasic;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mesh = SharpGLTF.Schema2.Mesh;


namespace Xande.GltfImporter {
    internal class SubmeshBuilder {
        public uint IndexCount => ( uint )Indices.LongCount();
        public List<uint> Indices = new();
        public int BoneCount { get; } = 0;
        public List<string> Attributes = new();
        public List<string> Shapes => _shapeBuilders.Keys.ToList();
        private Dictionary<string, ShapeBuilder> _shapeBuilders = new();
        private Mesh _mesh;
        public string MaterialPath = String.Empty;
        private string _material = String.Empty;

        public Vector4 MinBoundingBox = new( 9999f, 9999f, 9999f, -9999f );
        public Vector4 MaxBoundingBox = new( -9999f, -9999f, -9999f, 9999f );

        // Does not include shape vertices
        private int _vertexCount = 0;

        public Dictionary<int, string> OriginalBoneIndexToStrings = new();
        public List<(List<Vector3>, float)> AppliedShapes = new();
        public List<(List<Vector3>, float)> AppliedShapesNormals = new();

        private List<Vector4>? _bitangents = null;
        private ILogger? _logger;
        private VertexDataBuilder VertexDataBuilder;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="skeleton"></param>
        public SubmeshBuilder( Mesh mesh, List<string> skeleton, MdlStructs.VertexDeclarationStruct vertexDeclarationStruct, ILogger? logger = null ) {
            _logger = logger;
            _mesh = mesh;
            if( _mesh.Primitives.Count == 0 ) {
                _logger?.Error( $"Submesh had zero primitives" );
            }
            if( _mesh.Primitives.Count > 1 ) {
                _logger?.Warning( $"Submesh had more than one primitive." );
            }

            var primitive = _mesh.Primitives[0];
            //foreach( var primitive in _mesh.Primitives ) {
            VertexDataBuilder = new( primitive, vertexDeclarationStruct, _logger );
            Indices.AddRange( primitive.GetIndices() );

            var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
            var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
            var material = primitive.Material?.Name;

            if( String.IsNullOrEmpty( material ) ) {
                // TODO: Figure out what to do in this case
                // Have a Model as an argument and take the first material from that?
                _logger?.Error( "Submesh had null material name" );
            }
            else {
                if( String.IsNullOrEmpty( MaterialPath ) ) {
                    _material = material;
                    //MaterialPath = AdjustMaterialPath( _material );
                    MaterialPath = _material;
                }
                else {
                    if( material != _material || material != MaterialPath ) {
                        _logger?.Error( $"Found more than one material name. Original: \"{MaterialPath}\" vs \"{material}\"" );
                    }
                }
            }

            if( positions != null ) {
                _vertexCount += positions.Count;

                foreach( var pos in positions ) {
                    MinBoundingBox.X = MinBoundingBox.X < pos.X ? MinBoundingBox.X : pos.X;
                    MinBoundingBox.Y = MinBoundingBox.Y < pos.Y ? MinBoundingBox.Y : pos.Y;
                    MinBoundingBox.Z = MinBoundingBox.Z < pos.Z ? MinBoundingBox.Z : pos.Z;

                    MaxBoundingBox.X = MaxBoundingBox.X > pos.X ? MaxBoundingBox.X : pos.X;
                    MaxBoundingBox.Y = MaxBoundingBox.Y > pos.Y ? MaxBoundingBox.Y : pos.Y;
                    MaxBoundingBox.Z = MaxBoundingBox.Z > pos.Z ? MaxBoundingBox.Z : pos.Z;
                }
            }
            else {
                _logger?.Error( "This submesh had no positions." );
            }

            var includeNHara = skeleton.Where( x => x.StartsWith( "n_hara" ) ).Count() > 1;

            if( blendIndices != null ) {
                foreach( var blendIndex in blendIndices ) {
                    for( var i = 0; i < 4; i++ ) {
                        var index = ( int )blendIndex[i];
                        if( !OriginalBoneIndexToStrings.ContainsKey( index ) && index < skeleton.Count &&
                            skeleton[index] != "n_root" && ( skeleton[index] != "n_hara" || includeNHara ) ) {
                            OriginalBoneIndexToStrings.Add( index, skeleton[index] );
                        }
                    }
                }
            }

            var shapeWeights = mesh.GetMorphWeights();
            try {
                var json = mesh.Extras.Content;
                if( json != null ) {
                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            for( var i = 0; i < names.Count; i++ ) {
                                var shapeName = names[i]?.ToString();
                                if( shapeName == null ) { continue; }

                                if( shapeName.StartsWith( "shp_" ) ) {
                                    _shapeBuilders[shapeName] = new ShapeBuilder( shapeName, primitive, i, vertexDeclarationStruct, _logger );
                                    VertexDataBuilder.AddShape( shapeName, primitive.GetMorphTargetAccessors( i ) );
                                }
                                else if( shapeName.StartsWith( "atr_" ) && !Attributes.Contains( shapeName ) ) {
                                    Attributes.Add( shapeName );
                                }
                                else {
                                    var shapeWeight = shapeWeights[i];
                                    if( shapeWeight == 0 ) { continue; }

                                    var target = primitive.GetMorphTargetAccessors( i );
                                    if( target == null ) { continue; }
                                    target.TryGetValue( "POSITION", out var shapeAccessor );
                                    target.TryGetValue( "NORMAL", out var shapeNormalAccessor );
                                    var appliedPositions = shapeAccessor?.AsVector3Array();
                                    var appliedNormalPositions = shapeNormalAccessor?.AsVector3Array();

                                    if( appliedPositions != null && appliedPositions.Any() && appliedPositions.Where( x => x != Vector3.Zero ).Any() ) {
                                        _logger?.Debug( $"AppliedShape: {shapeName} with weight {shapeWeight}" );
                                        AppliedShapes.Add( (appliedPositions.ToList(), shapeWeight) );
                                    }
                                    if( appliedNormalPositions != null && appliedNormalPositions.Any() && appliedNormalPositions.Where( x => x != Vector3.Zero ).Any() ) {
                                        // Unsure if this actually matters
                                        AppliedShapesNormals.Add( (appliedNormalPositions.ToList(), shapeWeight) );
                                    }
                                }
                            }
                        }
                    }
                }
                else {
                    _logger?.Debug( "Mesh contained no extras." );
                }
            }
            catch( Exception ex ) {
                _logger?.Error( "Could not add shapes." );
                _logger?.Error( ex.ToString() );
            }
            //}
            VertexDataBuilder.AppliedShapePositions = AppliedShapes;
            VertexDataBuilder.AppliedShapeNormals = AppliedShapesNormals;

            BoneCount = OriginalBoneIndexToStrings.Keys.Count;
        }

        public void SetBlendIndicesDict( Dictionary<int, int> dict ) {
            VertexDataBuilder.BlendIndicesDict = dict;
        }

        public List<ushort> GetSubmeshBoneMap( List<string> bones ) {
            var ret = new List<ushort>();
            foreach( var val in OriginalBoneIndexToStrings.Values ) {
                var index = bones.IndexOf( val );
                ret.Add( ( ushort )index );
            }
            return ret;
        }

        public int GetVertexCount( bool includeShapes = false, List<string>? strings = null ) {
            var ret = _vertexCount;
            if( includeShapes ) {
                foreach( var shapeName in _shapeBuilders.Keys ) {
                    if( strings == null || strings.Contains( shapeName ) ) {
                        ret += _shapeBuilders[shapeName].VertexCount;
                    }
                }
            }
            return ret;
        }

        public int GetShapeVertexCount( string str ) {
            foreach( var shapeName in _shapeBuilders.Keys ) {
                if( str == shapeName ) {
                    return _shapeBuilders[str].VertexCount;
                }
            }
            return 0;
        }

        public uint GetAttributeIndexMask( List<string> attributes ) {
            var ret = 0;
            for( var i = 0; i < attributes.Count; i++ ) {
                if( Attributes.Contains( attributes[i] ) ) {
                    ret += ( 1 << i );
                }
            }
            return ( uint )ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="indexOffset">The value to add to all index values</param>
        /// <returns></returns>
        public List<byte> GetIndexData( int indexOffset = 0 ) {
            var ret = new List<byte>();
            foreach( var index in Indices ) {
                ret.AddRange( BitConverter.GetBytes( ( ushort )( index + indexOffset ) ) );
            }
            return ret;
        }

        public static string AdjustMaterialPath( string mat ) {
            var ret = mat;
            // TODO: More nuanced method for adjusting the material path
            // furniture paths are the entire path
            if( !mat.StartsWith( "/" ) ) {
                ret = "/" + ret;
            }
            if( !mat.EndsWith( ".mtrl" ) ) {
                ret += ".mtrl";
            }
            return ret;
        }

        public bool AddAttribute( string name ) {
            if( !Attributes.Contains( name ) ) {
                Attributes.Add( name );
                return true;
            }
            return false;
        }

        // TODO: Do we actually need to calculate these values?
        public void CalculateBitangents( bool forceRecalculate = false ) {
            if( _bitangents != null && !forceRecalculate ) {
                return;
            }

            try {
                var tris = _mesh.EvaluateTriangles();
                var indices = _mesh.Primitives[0].GetIndices();
                _bitangents = new List<Vector4>();
                var positions = _mesh.Primitives[0].GetVertexAccessor( "POSITION" )?.AsVector3Array();
                var uvs = _mesh.Primitives[0].GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array().ToList();
                var normals = _mesh.Primitives[0].GetVertexAccessor( "NORMAL" )?.AsVector3Array().ToList();
                var colors = _mesh.Primitives[0].GetVertexAccessor( "COLOR_0" )?.AsVector4Array().ToList();

                var binormalDict = new SortedDictionary<int, Vector4>();
                var tangentDict = new SortedDictionary<int, Vector3>();

                // https://github.com/TexTools/xivModdingFramework/blob/f8d442688e61851a90646e309b868783c47122be/xivModdingFramework/Models/Helpers/ModelModifiers.cs#L1575
                var connectedVertices = new Dictionary<int, HashSet<int>>();
                for( var i = 0; i < indices.Count; i += 3 ) {
                    var t0 = ( int )indices[i];
                    var t1 = ( int )indices[i + 1];
                    var t2 = ( int )indices[i + 2];

                    if( !connectedVertices.ContainsKey( t0 ) ) {
                        connectedVertices.Add( t0, new HashSet<int>() );
                    }
                    if( !connectedVertices.ContainsKey( t1 ) ) {
                        connectedVertices.Add( t1, new HashSet<int>() );
                    }
                    if( !connectedVertices.ContainsKey( t2 ) ) {
                        connectedVertices.Add( t2, new HashSet<int>() );
                    }

                    connectedVertices[t0].Add( t1 );
                    connectedVertices[t0].Add( t2 );
                    connectedVertices[t1].Add( t0 );
                    connectedVertices[t1].Add( t2 );
                    connectedVertices[t2].Add( t0 );
                    connectedVertices[t2].Add( t1 );
                }

                var vertTranslation = new Dictionary<int, int>();
                var weldedVerts = new Dictionary<int, List<int>>();
                var tempVertices = new List<int>();

                for( var oIdx = 0; oIdx < positions?.Count; oIdx++ ) {
                    var idx = -1;
                    for( var nIdx = 0; nIdx < tempVertices.Count; nIdx++ ) {
                        if( positions[nIdx] == positions[oIdx]
                            && uvs?[nIdx] == uvs?[oIdx]
                            && normals?[nIdx] == normals?[oIdx]
                            && colors?[nIdx] != colors?[oIdx] ) {
                            var alreadyMergedVerts = weldedVerts[nIdx];
                            var alreadyConnectedOldVerts = new HashSet<int>();
                            foreach( var amIdx in alreadyMergedVerts ) {
                                foreach( var cv in connectedVertices[amIdx] ) {
                                    alreadyConnectedOldVerts.Add( cv );
                                }
                            }

                            var myConnectedVerts = connectedVertices[oIdx];
                            var isMirror = false;
                            foreach( var weldedConnection in alreadyConnectedOldVerts ) {
                                foreach( var newConnection in myConnectedVerts ) {
                                    if( uvs[newConnection] == uvs[weldedConnection] &&
                                        positions[newConnection] != positions[weldedConnection] ) {
                                        isMirror = true;
                                        break;
                                    }
                                }
                                if( isMirror ) {
                                    break;
                                }
                            }

                            if( !isMirror ) {
                                idx = nIdx;
                                break;
                            }
                        }
                    }
                    if( idx == -1 ) {
                        tempVertices.Add( oIdx );
                        idx = tempVertices.Count - 1;
                        weldedVerts.Add( idx, new List<int>() );
                    }

                    weldedVerts[idx].Add( oIdx );
                    vertTranslation.Add( oIdx, idx );
                }

                var tempIndices = new List<int>();
                for( var i = 0; i < indices.Count; i++ ) {
                    var oldVert = indices[i];
                    var newVert = vertTranslation[( int )oldVert];
                    tempIndices.Add( newVert );
                }

                var tangents = new List<Vector3>( tempVertices.Count );
                tangents.AddRange( Enumerable.Repeat( Vector3.Zero, tempVertices.Count ) );
                var bitangents = new List<Vector3>( tempVertices.Count );
                bitangents.AddRange( Enumerable.Repeat( Vector3.Zero, tempVertices.Count ) );

                for( var a = 0; a < tempIndices.Count; a += 3 ) {
                    // applied shapes?
                    var vertexId1 = tempIndices[a];
                    var vertexId2 = tempIndices[a + 1];
                    var vertexId3 = tempIndices[a + 2];

                    var vertex1 = tempVertices[vertexId1];
                    var vertex2 = tempVertices[vertexId2];
                    var vertex3 = tempVertices[vertexId3];

                    var deltaX1 = positions[vertex2].X - positions[vertex1].X;
                    var deltaX2 = positions[vertex3].X - positions[vertex1].X;

                    var deltaY1 = positions[vertex2].Y - positions[vertex1].Y;
                    var deltaY2 = positions[vertex3].Y - positions[vertex1].Y;

                    var deltaZ1 = positions[vertex2].Z - positions[vertex1].Z;
                    var deltaZ2 = positions[vertex3].Z - positions[vertex1].Z;

                    var deltaU1 = uvs[vertex2].X - uvs[vertex1].X;
                    var deltaU2 = uvs[vertex3].X - uvs[vertex1].X;

                    var deltaV1 = uvs[vertex2].Y - uvs[vertex1].Y;
                    var deltaV2 = uvs[vertex3].Y - uvs[vertex1].Y;

                    var r = 1.0f / ( deltaU1 * deltaV2 - deltaU2 * deltaV1 );
                    var sdir = new Vector3( ( deltaV2 * deltaX1 - deltaV1 * deltaX2 ) * r, ( deltaV2 * deltaY1 - deltaV1 * deltaY2 ) * r, ( deltaV2 * deltaZ1 - deltaV1 * deltaZ2 ) * r );
                    var tdir = new Vector3( ( deltaU1 * deltaX2 - deltaU2 * deltaX1 ) * r, ( deltaU1 * deltaY2 - deltaU2 * deltaY1 ) * r, ( deltaU1 * deltaZ2 - deltaU2 * deltaZ1 ) * r );

                    tangents[vertexId1] += sdir;
                    tangents[vertexId2] += sdir;
                    tangents[vertexId3] += sdir;

                    bitangents[vertexId1] += tdir;
                    bitangents[vertexId2] += tdir;
                    bitangents[vertexId3] += tdir;
                }

                for( var vertexId = 0; vertexId < tempVertices.Count; vertexId++ ) {
                    var vertex = tempVertices[vertexId];
                    var oVertices = vertTranslation.Where( x => x.Value == vertexId ).Select( x => x.Key ).ToList();

                    var n = normals[vertex];
                    var t = tangents[vertexId];
                    var b = bitangents[vertexId];

                    var tangent = t - ( n * Vector3.Dot( n, t ) );
                    tangent = Vector3.Normalize( tangent );

                    var binormal = Vector3.Cross( n, tangent );
                    binormal = Vector3.Normalize( binormal );
                    var handedness = Vector3.Dot( Vector3.Cross( t, b ), n ) > 0 ? 1 : -1;
                    binormal *= handedness;

                    _bitangents.Add( new Vector4( binormal, handedness ) );

                    foreach( var vIdx in oVertices ) {
                        if( !binormalDict.ContainsKey( vIdx ) ) {
                            binormalDict.Add( vIdx, new Vector4( binormal, handedness ) );
                        }
                        if( !tangentDict.ContainsKey( vIdx ) ) {
                            tangentDict.Add( vIdx, tangent );
                        }
                    }

                }
            }
            catch( Exception ex ) {
                _logger?.Error( $"Could not calculate bitangents. {ex}" );
            }
        }

        public Dictionary<int, List<byte>> GetVertexData() {
            CalculateBitangents();
            //VertexDataBuilder.Bitangents = _bitangents;
            VertexDataBuilder.SetBitangents( _bitangents );
            return VertexDataBuilder.GetVertexData();
        }

        public IDictionary<string, Dictionary<int, List<byte>>> GetShapeVertexData( List<string>? strings = null ) {
            var ret = new Dictionary<string, Dictionary<int, List<byte>>>();

            foreach( var shapeName in _shapeBuilders.Keys ) {
                if( strings == null || strings.Contains( shapeName ) ) {
                    ret.Add( shapeName, VertexDataBuilder.GetShapeVertexData( _shapeBuilders[shapeName].DifferentVertices, shapeName ) );
                }
            }
            return ret;
        }

        public Dictionary<int, List<byte>> GetShapeVertexData( string shapeName ) {
            if( _shapeBuilders.ContainsKey( shapeName ) ) {
                return VertexDataBuilder.GetShapeVertexData( _shapeBuilders[shapeName].DifferentVertices );
            }
            return new Dictionary<int, List<byte>>();

        }

        public List<MdlStructs.ShapeValueStruct> GetShapeValues( string str ) {
            if( _shapeBuilders.ContainsKey( str ) ) {
                return _shapeBuilders[str].ShapeValues;
            }
            return new List<MdlStructs.ShapeValueStruct>();
        }
    }
}
