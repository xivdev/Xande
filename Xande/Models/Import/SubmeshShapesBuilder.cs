using Dalamud.Logging;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Microsoft.VisualBasic;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class SubmeshShapesBuilder {
        public Dictionary<string, int> Shapes = new();
        private SubmeshBuilder _parent;

        private Mesh _mesh;
        private Dictionary<string, List<MdlStructs.ShapeValueStruct>> _shapeValues = new();
        private Dictionary<string, List<int>> _differentVertices = new();
        private Dictionary<string, IReadOnlyDictionary<string, Accessor>> _accessors = new();

        public Dictionary<string, List<MdlStructs.ShapeValueStruct>> ShapeValues = new();

        public SubmeshShapesBuilder( SubmeshBuilder parent, Mesh mesh ) {
            _parent = parent;
            _mesh = mesh;

            var shapeIndices = new List<int>();
            foreach( var primitive in _mesh.Primitives ) {
                try {
                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            for( var i = 0; i < names.Count; i++ ) {
                                var n = names[i];
                                if(n != null && n.ToString().StartsWith("shp_") && !Shapes.ContainsKey( n.ToString() ) ) {
                                    shapeIndices.Add( i );
                                    Shapes.Add( n.ToString(), i );
                                    _shapeValues.Add( n.ToString(), new() );
                                    _differentVertices.Add( n.ToString(), new() );
                                    _accessors.Add( n.ToString(), new Dictionary<string, Accessor>() );

                                }
                            }
                        }
                    }
                    else {
                        PluginLog.Debug( "Mesh contained no extras." );
                    }
                }
                catch( Exception ex ) {

                }


                if( Shapes.Count > 0 && primitive.MorphTargetsCount == Shapes.Count ) {
                    foreach( var shapeName in Shapes.Keys ) {
                        var shape = primitive.GetMorphTargetAccessors( Shapes[shapeName] );
                        _accessors[shapeName] = shape;

                        var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
                        var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

                        var shapePositions = positionsAccessor?.AsVector3Array();
                        var shapeNormals = normalsAccessor?.AsVector3Array();

                        if( shapePositions != null && shapeNormals != null ) {
                            for( var j = 0; j < shapePositions.Count; j++ ) {
                                if( shapePositions[j] != Vector3.Zero ) {
                                    _differentVertices[shapeName].Add( j );
                                }
                            }
                            try {
                                var indices = primitive.GetIndices();
                                if( indices != null ) {
                                    for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                                        var index = indices[indexIdx];
                                        if( _differentVertices[shapeName].Contains( ( int )index ) ) {
                                            _shapeValues[shapeName].Add( new() {
                                                BaseIndicesIndex = ( ushort )( indexIdx ),
                                                // Later, we will have to add the total number of vertices (and vertices added via shapes) that come before this Submesh
                                                ReplacingVertexIndex = ( ushort )( _differentVertices[shapeName].IndexOf( ( int )index ) )
                                                // That is to say, that these values are relative to the Submesh
                                            } );
                                        }
                                    }
                                }
                            }
                            catch( Exception ex ) {
                                PluginLog.Error( $"Could not get indices. {ex.Message}" );
                            }
                        }
                    }
                }
            }
        }

        public int GetVertexCount( List<string>? strings = null ) {
            var vertexCount = 0;
            foreach (var shapeName in Shapes.Keys) {
                if(strings == null || strings.Contains( shapeName )) {
                    vertexCount += _differentVertices[shapeName].Count;
                }
            }
            return vertexCount;
        }

        public Dictionary<string, Dictionary<int, List<byte>>> GetVertexData( MdlStructs.VertexDeclarationStruct vertexDeclarations, List<string>? strings = null, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            var ret = new Dictionary<string, Dictionary<int, List<byte>>>();
            foreach( var shapeName in Shapes.Keys ) {
                if( strings == null || strings.Contains( shapeName ) ) {
                    var accessors = _accessors[shapeName];
                    var diff = _differentVertices[shapeName];
                    //PluginLog.Debug( $"{shapeName} - {diff.Count}" );
                    var shapeVertexData = VertexDataBuilder.GetShapeVertexData( _parent, vertexDeclarations, accessors, diff, blendIndicesDict, bitangents );
                    ret.Add( shapeName, shapeVertexData );
                }
            }
            return ret;
        }

        public List<MdlStructs.ShapeValueStruct> GetShapeValues( string str ) {
            if( ShapeValues.ContainsKey( str ) ) {
                return ShapeValues[str];
            }
            return new List<MdlStructs.ShapeValueStruct>();
        }
    }
}
