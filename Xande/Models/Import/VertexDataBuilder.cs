using Dalamud.Logging;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class VertexDataBuilder {
        public Dictionary<int, int>? BlendIndicesDict = null;
        public IReadOnlyDictionary<string, Accessor>? ShapeAccessor = null;
        public List<Vector4>? Bitangents = null;

        private List<Vector3>? _positions = null;
        private List<Vector4>? _blendWeights = null;
        private List<Vector4>? _blendIndices = null;
        private List<Vector3>? _normals = null;
        private List<Vector2>? _texCoords = null;
        private List<Vector4>? _tangent1 = null;
        private List<Vector4>? _colors = null;

        private MdlStructs.VertexDeclarationStruct _vertexDeclaration;

        public VertexDataBuilder( MeshPrimitive primitive, MdlStructs.VertexDeclarationStruct vertexDeclaration ) {
            _vertexDeclaration = vertexDeclaration;
            _positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array().ToList();
            _blendWeights = primitive.GetVertexAccessor( "WEIGHTS_0" )?.AsVector4Array().ToList();
            _blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array().ToList();
            _normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array().ToList();
            _texCoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array().ToList();
            _tangent1 = primitive.GetVertexAccessor( "TANGENT" )?.AsVector4Array().ToList();
            _tangent1 = primitive.GetVertexAccessor( "COLOR_0" )?.AsVector4Array().ToList();
        }

        public List<byte> GetVertexData( int index, MdlStructs.VertexElement ve, bool getShapeData = false ) {
            return GetBytes( GetVector4( index, ( Vertex.VertexUsage )ve.Usage, getShapeData ), ( Vertex.VertexType )ve.Type );
        }

        public Dictionary<int, List<byte>> GetVertexData( bool getShapeData = false ) {
            var streams = new Dictionary<int, List<byte>>();
            for( var vertexId = 0; vertexId < _positions?.Count; vertexId++ ) {
                foreach( var ve in _vertexDeclaration.VertexElements ) {
                    if( ve.Stream == 255 ) break;
                    if( !streams.ContainsKey( ve.Stream ) ) {
                        streams.Add( ve.Stream, new List<byte>() );
                    }

                    streams[ve.Stream].AddRange( GetVertexData( vertexId, ve, getShapeData ) );
                }
            }

            return streams;
        }

        public Dictionary<int, List<byte>> GetShapeVertexData( List<int> diffVertices ) {
            var streams = new Dictionary<int, List<byte>>();
            if (ShapeAccessor == null) {
                PluginLog.Debug( $"Shape accessor was null" );
            }

            foreach( var vertexId in diffVertices ) {
                foreach( var ve in _vertexDeclaration.VertexElements ) {
                    if( ve.Stream == 255 ) { break; }
                    if( !streams.ContainsKey( ve.Stream ) ) {
                        streams.Add( ve.Stream, new List<byte>() );
                    }

                    streams[ve.Stream].AddRange( GetVertexData( vertexId, ve, true ) );
                }
            }

            return streams;
        }

        private List<Vector3>? GetShapePositions() {
            Accessor? accessor = null;
            ShapeAccessor?.TryGetValue( "POSITION", out accessor );
            return accessor?.AsVector3Array().ToList();
        }

        private List<Vector3>? GetShapeNormals() {
            Accessor? accessor = null;
            ShapeAccessor?.TryGetValue( "NORMAL", out accessor );
            return accessor?.AsVector3Array().ToList();
        }

        private Vector4 GetVector4( int index, Vertex.VertexUsage usage, bool getShapeData = false ) {
            var vector4 = new Vector4( 0, 0, 0, 0 );
            switch( usage ) {
                case Vertex.VertexUsage.Position:
                    if( _positions != null ) {
                        vector4 = new Vector4( _positions[index], 0 );
                        var shapePositions = GetShapePositions();
                        if( getShapeData && shapePositions != null ) {
                            vector4 += new Vector4( shapePositions[index], 0 );
                        }
                    }
                    break;
                case Vertex.VertexUsage.BlendWeights:
                    vector4 = _blendWeights?[index] ?? vector4;
                    break;
                case Vertex.VertexUsage.BlendIndices:
                    if( _blendIndices == null ) break;
                    vector4 = _blendIndices[index];
                    if( BlendIndicesDict != null ) {
                        for( var i = 0; i < 4; i++ ) {
                            if( BlendIndicesDict.ContainsKey( ( int )_blendIndices[index][i] ) ) {
                                vector4[i] = BlendIndicesDict[( int )_blendIndices[index][i]];
                            }
                        }
                    }
                    break;
                case Vertex.VertexUsage.Normal:
                    if( _normals != null ) {
                        vector4 = new Vector4( _normals[index], 0 );

                        var shapeNormals = GetShapeNormals();
                        if( getShapeData && shapeNormals != null ) {
                            vector4 += new Vector4( shapeNormals[index], 0 );
                        }
                    }
                    else {
                        vector4 = new( 1, 1, 1, 1 );
                    }
                    break;
                case Vertex.VertexUsage.UV:
                    if( _texCoords != null ) {
                        vector4 = new( _texCoords[index], 0, 0 );
                    }
                    break;
                case Vertex.VertexUsage.Tangent2:
                    break;
                case Vertex.VertexUsage.Tangent1:
                    vector4 = _tangent1?[index] ?? vector4;
                    break;
                case Vertex.VertexUsage.Color:
                    if( _colors != null ) {
                        vector4 = _colors[index];
                    }
                    else {
                        vector4 = new( 1, 1, 1, 1 );
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

        /*
        public static List<byte> GetVertexData( MeshPrimitive primitive, int index, List<List<Vector3>> appliedShapes, MdlStructs.VertexElement ve, Dictionary<int, int>? blendIndicesDict = null, IReadOnlyDictionary<string, Accessor>? shapeAccessor = null, List<Vector4>? bitangents = null ) {
            var vector4 = GetVector4( primitive, index, ( Vertex.VertexUsage )ve.Usage, appliedShapes, blendIndicesDict, shapeAccessor, bitangents );
            return GetBytes( vector4, ( Vertex.VertexType )ve.Type );
        }
        private static Vector4 GetVector4( MeshPrimitive primitive, int index, Vertex.VertexUsage usage, List<List<Vector3>> appliedShapes,
            Dictionary<int, int>? blendIndexDict = null, IReadOnlyDictionary<string, Accessor>? shapeAccessor = null,
            List<Vector4>? bitangents = null ) {
            var vector4 = new Vector4( 0, 0, 0, 0 );

            switch( usage ) {
                case Vertex.VertexUsage.Position:
                    Accessor? positionAccessor = null;
                    shapeAccessor?.TryGetValue( "POSITION", out positionAccessor );
                    var shapePositions = positionAccessor?.AsVector3Array();
                    var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                    if( positions != null && positions.Count > index ) {
                        vector4 = new Vector4( positions[index], 1 );
                        if( shapePositions != null ) {
                            var shapePos = new Vector4( shapePositions[index], 0 );
                            vector4 += shapePos;
                        }
                    }
                    foreach( var list in appliedShapes ) {
                        if( list.Count > index ) {
                            vector4 += new Vector4( list[index], 0 );
                        }
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
                    var shapeNormals = normalAccessor?.AsVector3Array();
                    var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
                    if( normals != null && normals.Count > index ) {
                        vector4 = new Vector4( normals[index], 0 );
                        if( shapeNormals != null ) {
                            var shapeNor = new Vector4( shapeNormals[index], 0 );
                            vector4 += shapeNor;
                        }
                    }
                    else {
                        vector4 = new( 1, 1, 1, 1 );
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

        public static Dictionary<int, List<byte>> GetShapeVertexData( SubmeshBuilder submesh, MdlStructs.VertexDeclarationStruct vertexDeclarations, IReadOnlyDictionary<string, Accessor>? shapeAccessor, List<int> diffVertices, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            var streams = new Dictionary<int, List<byte>>();

            foreach( var primitive in submesh.Mesh.Primitives ) {
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                if( positions != null ) {
                    foreach( var vertexId in diffVertices ) {
                        for( var declarationId = 0; declarationId < vertexDeclarations.VertexElements.Length; declarationId++ ) {
                            var ve = vertexDeclarations.VertexElements[declarationId];
                            if( ve.Stream == 255 ) { break; }
                            if( !streams.ContainsKey( ve.Stream ) ) {
                                streams.Add( ve.Stream, new List<byte>() );
                            }
                            var currStream = streams[ve.Stream];

                            streams[ve.Stream].AddRange( GetVertexData( primitive, vertexId, submesh.AppliedShapes, ve, blendIndicesDict, shapeAccessor, bitangents ) );
                        }
                    }
                }
            }
            return streams;
        }

        public static Dictionary<int, List<byte>> GetVertexData( SubmeshBuilder submesh, MdlStructs.VertexDeclarationStruct vertexDeclarations, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            var streams = new Dictionary<int, List<byte>>();

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

                            streams[ve.Stream].AddRange( GetVertexData( primitive, vertexId, submesh.AppliedShapes, ve, blendIndicesDict, null, bitangents ) );
                        }
                    }
                }
            }

            return streams;
        }
        */
    }
}
