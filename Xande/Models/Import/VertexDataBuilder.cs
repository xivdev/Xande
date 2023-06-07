using Dalamud.Logging;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class VertexDataBuilder {

        private static List<byte> GetVertexData( MeshPrimitive primitive, int index, MdlStructs.VertexElement ve, Dictionary<int, int>? blendIndicesDict = null, IReadOnlyDictionary<string, Accessor>? shapeAccessor = null, List<Vector4>? bitangents = null ) {
            var vector4 = GetVector4( primitive, index, ( Vertex.VertexUsage )ve.Usage, blendIndicesDict, shapeAccessor, bitangents );
            return GetBytes( vector4, ( Vertex.VertexType )ve.Type );
        }
        private static Vector4 GetVector4( MeshPrimitive primitive, int index, Vertex.VertexUsage usage,
            Dictionary<int, int>? blendIndexDict = null, IReadOnlyDictionary<string, Accessor>? shapeAccessor = null,
            List<Vector4>? bitangents = null ) {
            var vector4 = new Vector4( 0, 0, 0, 0 );

            switch( usage ) {
                case Vertex.VertexUsage.Position:
                    Accessor? positionAccessor = null;
                    shapeAccessor?.TryGetValue( "POSITION", out positionAccessor );
                    if( positionAccessor != null ) {
                        PluginLog.Debug( "shape is active" );
                    }
                    var positions = positionAccessor?.AsVector3Array() ?? primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                    if( positions != null && positions.Count > index ) {
                        vector4 = new Vector4( positions[index], 1 );
                    }
                    break;
                case Vertex.VertexUsage.BlendWeights:
                    var blendWeights = primitive.GetVertexAccessor( "WEIGHTS_0" )?.AsVector4Array();
                    if( blendWeights != null && blendWeights.Count > index ) {
                        vector4 = blendWeights[index];
                    }
                    break;
                case Vertex.VertexUsage.BlendIndices:
                    var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
                    if( blendIndices != null && blendIndices.Count > index ) {
                        vector4 = blendIndices[index];
                        if( blendIndexDict != null ) {
                            for( var i = 0; i < 4; i++ ) {
                                if( blendIndexDict.ContainsKey( ( int )blendIndices[index][i] ) ) {
                                    vector4[i] = blendIndexDict[( int )blendIndices[index][i]];
                                }
                            }
                        }
                    }
                    break;
                case Vertex.VertexUsage.Normal:
                    Accessor? normalAccessor = null;
                    shapeAccessor?.TryGetValue( "NORMAL", out normalAccessor );
                    var normals = normalAccessor?.AsVector3Array() ?? primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
                    if( normals != null && normals.Count > index ) {
                        vector4 = new Vector4( normals[index], 0 );
                    }
                    break;
                case Vertex.VertexUsage.UV:
                    var texCoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array();
                    if( texCoords?.Count > index ) {
                        vector4 = new( texCoords[index].X, texCoords[index].Y, 0, 0 );
                    }
                    break;
                case Vertex.VertexUsage.Tangent2:
                    // ??
                    vector4 = new( 0, 0, 0, 0 );
                    break;
                case Vertex.VertexUsage.Tangent1:
                    if( bitangents != null ) {
                        var vector3 = new Vector3( bitangents[index].X, bitangents[index].Y, bitangents[index].Z );
                        vector3 = Vector3.Normalize( vector3 );
                        vector3 += Vector3.One;
                        vector3 /= 2.0f;
                        vector3 -= Vector3.One;
                        var handedness = bitangents[index].W > 0 ? 0 : 1;
                        vector4 = new Vector4( vector3, handedness );
                    }
                    else {
                        var tangents = primitive.GetVertexAccessor( "TANGENT" )?.AsVector4Array();
                        if( tangents?.Count > index ) {
                            vector4 = tangents[index];
                        }
                    }
                    break;
                case Vertex.VertexUsage.Color:
                    var colors = primitive.GetVertexAccessor( "COLOR_0" )?.AsVector4Array();
                    if( colors?.Count > index ) {
                        vector4 = colors[index];
                    }
                    else {
                        vector4 = new Vector4( 1, 1, 1, 1 );
                    }
                    break;
            }
            return vector4;
        }

        private static List<byte> GetBytes( Vector4 vector4, Vertex.VertexType type ) {
            var ret = new List<byte>();
            switch( type ) {
                case Vertex.VertexType.Single3:
                    ret.AddRange( BitConverter.GetBytes( vector4.X ) );
                    ret.AddRange( BitConverter.GetBytes( vector4.Y ) );
                    ret.AddRange( BitConverter.GetBytes( vector4.Z ) );
                    break;
                case Vertex.VertexType.Single4:
                    ret.AddRange( BitConverter.GetBytes( vector4.X ) );
                    ret.AddRange( BitConverter.GetBytes( vector4.Y ) );
                    ret.AddRange( BitConverter.GetBytes( vector4.Z ) );
                    ret.AddRange( BitConverter.GetBytes( vector4.W ) );
                    break;
                case Vertex.VertexType.UInt:
                    ret.Add( ( byte )vector4.X );
                    ret.Add( ( byte )vector4.Y );
                    ret.Add( ( byte )vector4.Z );
                    ret.Add( ( byte )vector4.W );
                    break;
                case Vertex.VertexType.ByteFloat4:
                    ret.Add( ( byte )Math.Round( vector4.X * 255f ) );
                    ret.Add( ( byte )Math.Round( vector4.Y * 255f ) );
                    ret.Add( ( byte )Math.Round( vector4.Z * 255f ) );
                    ret.Add( ( byte )Math.Round( vector4.W * 255f ) );
                    break;
                case Vertex.VertexType.Half2:
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                    break;
                case Vertex.VertexType.Half4:
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Z ) );
                    ret.AddRange( BitConverter.GetBytes( ( Half )vector4.W ) );
                    break;
            }
            return ret;

        }

        public static Dictionary<int, List<byte>> GetVertexData( SubmeshBuilder submesh, MdlStructs.VertexDeclarationStruct vertexDeclarations, List<string>? strings = null, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            var streams = new Dictionary<int, List<byte>>();
            var ret = new List<byte>();

            foreach( var primitive in submesh.Mesh.Primitives ) {
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();

                if( positions != null ) {
                    for( var vertexId = 0; vertexId < positions.Count; vertexId++ ) {
                        for( var declarationId = 0; declarationId < vertexDeclarations.VertexElements.Length; declarationId++ ) {
                            var ve = vertexDeclarations.VertexElements[declarationId];
                            if( ve.Stream == 255 ) { break; }
                            if( !streams.ContainsKey( ve.Stream ) ) {
                                streams.Add( ve.Stream, new List<byte>() );
                            }
                            var currStream = streams[ve.Stream];

                            streams[ve.Stream].AddRange( GetVertexData( primitive, vertexId, ve, blendIndicesDict, null, bitangents ) );
                        }
                    }
                }
            }

            /*
            var shapeAccessors = submesh.SubmeshShapeBuilder.GetActiveShapeAccessors( strings );
            foreach( var s in shapeAccessors ) {
                if( s.ContainsKey( "POSITION" ) ) {
                    foreach( var primitive in submesh.Mesh.Primitives ) {
                        var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                        s.TryGetValue( "POSITION", out var shapeAccessor );
                        var shapePositions = shapeAccessor?.AsVector3Array();
                        if( positions != null && shapePositions != null ) {
                            for( var vertexId = 0; vertexId < positions.Count; vertexId++ ) {
                                for( var declarationId = 0; declarationId < vertexDeclarations.VertexElements.Length; declarationId++ ) {
                                    var ve = vertexDeclarations.VertexElements[declarationId];
                                    if( ve.Stream == 255 ) { break; }

                                    if (shapePositions[vertexId] != Vector3.Zero) {
                                        ret.AddRange(GetVertexData(primitive, vertexId, ve, blendIndicesDict, s) );
                                    }
                                }
                            }
                        }
                    }
                }
            }
            */

            return streams;
        }
    }
}
