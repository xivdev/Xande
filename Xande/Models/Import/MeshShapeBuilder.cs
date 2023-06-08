using Dalamud.Logging;
using Lumina.Data.Parsing;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class MeshShapeBuilder {

        private Dictionary<string, List<MdlStructs.ShapeValueStruct>> ShapeValues = new();
        private Dictionary<string, MeshPrimitive> MeshPrimitives = new();

        private int _indexCount = 0;
        private int _vertexCount = 0;
        public MeshShapeBuilder() {
        }

        public void Add( Mesh mesh ) {
            var indexCount = 0;
            foreach( var primitive in mesh.Primitives ) {
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                var indices = primitive.GetIndices();
                indexCount += indices.Count;

                var submeshShapeList = new List<string>();
                var uniqueVertices = new Dictionary<string, List<int>>();

                try {
                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            foreach( var n in names ) {
                                submeshShapeList.Add( n.ToString() );
                                if( !uniqueVertices.ContainsKey( n.ToString() ) ) {
                                    uniqueVertices.Add( n.ToString(), new() );
                                }
                            }

                        }
                    }
                }
                catch( Exception ex ) {

                }

                if( submeshShapeList.Count > 0 && primitive.MorphTargetsCount == submeshShapeList.Count ) {
                    for( var i = 0; i < primitive.MorphTargetsCount; i++ ) {
                        var accumulatedVertices = 0;
                        var currShape = submeshShapeList[i];
                        if( !ShapeValues.ContainsKey( currShape ) ) {
                            ShapeValues.Add( currShape, new() );
                        }

                        var shape = primitive.GetMorphTargetAccessors( i );

                        var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
                        var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

                        var shapePositions = positionsAccessor?.AsVector3Array();
                        var shapeNormals = normalsAccessor?.AsVector3Array();

                        if( shapePositions != null && shapeNormals != null ) {
                            for( var j = 0; j < shapePositions.Count; j++ ) {
                                if( shapePositions[j] != Vector3.Zero ) {
                                    uniqueVertices[currShape].Add( j );
                                }
                            }

                            accumulatedVertices += uniqueVertices[currShape].Count;
                            if( indices != null ) {
                                for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                                    var index = indices[indexIdx];
                                    var currList = uniqueVertices[currShape];
                                    if( currList.Contains( ( int )index ) ) {
                                        var replacedVertex = uniqueVertices[currShape].IndexOf( ( int )index );
                                        ShapeValues[currShape].Add( new() {
                                            BaseIndicesIndex = ( ushort )( indexIdx + _indexCount),
                                            ReplacingVertexIndex = ( ushort )( replacedVertex )
                                        } );
                                    }
                                }
                            }
                        }
                        _vertexCount += accumulatedVertices;

                        PluginLog.Debug( $"{currShape} - {ShapeValues[currShape].Count}" );
                    }
                }
            }
            _indexCount += indexCount;
        }

        public List<MdlStructs.ShapeValueStruct> GetShapeValues( string str ) {
            if( ShapeValues.ContainsKey( str ) ) {
                return ShapeValues[str];
            }
            return new List<MdlStructs.ShapeValueStruct>();
        }

        /*
        public Dictionary<string, Dictionary<int, List<byte>>> GetShapeData(MdlStructs.VertexDeclarationStruct vertexDeclaration, List<string>? strings = null, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            foreach (var shapeName in MeshPrimitives.Keys) {
                var shouldInclude = strings == null || strings.Contains( shapeName );
                if (shouldInclude) {
                    var prim = MeshPrimitives[shapeName];
                    VertexDataBuilder.GetVertexData2(prim, vertexDeclaration, blendIndicesDict)
                }
            }
        }
        */
    }
}
