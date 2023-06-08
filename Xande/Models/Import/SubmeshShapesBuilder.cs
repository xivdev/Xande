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
        public List<string> Shapes = new();
        private SubmeshBuilder _parent;

        private Mesh _mesh;
        private List<List<MdlStructs.ShapeValueStruct>> _shapeValues = new();
        private List<List<int>> _differentVertices = new();
        private List<IReadOnlyDictionary<string, Accessor>> _accessors = new();

        public Dictionary<string, List<MdlStructs.ShapeValueStruct>> ShapeValues = new();

        public SubmeshShapesBuilder( SubmeshBuilder parent, Mesh mesh ) {
            _parent = parent;
            _mesh = mesh;

            foreach( var primitive in _mesh.Primitives ) {
                try {
                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            foreach( var n in names ) {

                                if( !Shapes.Contains( n.ToString() ) ) {
                                    Shapes.Add( n.ToString() );
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
                    for( var i = 0; i < primitive.MorphTargetsCount; i++ ) {
                        var shape = primitive.GetMorphTargetAccessors( i );
                        _accessors.Add( shape );

                        var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
                        var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

                        var shapePositions = positionsAccessor?.AsVector3Array();
                        var shapeNormals = normalsAccessor?.AsVector3Array();

                        if( shapePositions != null && shapeNormals != null ) {
                            _differentVertices.Add( new() );
                            for( var j = 0; j < shapePositions.Count; j++ ) {
                                if( shapePositions[j] != Vector3.Zero ) {
                                    _differentVertices[i].Add( j );
                                }
                            }
                            _shapeValues.Add( new() );
                            try {
                                var indices = primitive.GetIndices();
                                if( indices != null ) {
                                    for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                                        var index = indices[indexIdx];
                                        if( _differentVertices[i].Contains( ( int )index ) ) {
                                            _shapeValues[i].Add( new() {
                                                BaseIndicesIndex = ( ushort )( indexIdx ),
                                                // Later, we will have to add the total number of vertices (and vertices added via shapes) that come before this Submesh
                                                ReplacingVertexIndex = ( ushort )( _differentVertices[i].IndexOf( ( int )index ) )
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

            for (var i =0; i < _differentVertices.Count; i++ ) {
                PluginLog.Debug($"diff[{i}] - {_differentVertices[i].Count}");
            }
        }

        public int GetVertexCount( List<string>? strings = null ) {
            var vertexCount = 0;
            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = strings == null || strings.Contains( Shapes[i] );

                if( includeShape ) {
                    vertexCount += _differentVertices[i].Count;
                }
            }
            return vertexCount;
        }

        public Dictionary<string, Dictionary<int, List<byte>>> GetVertexData( MdlStructs.VertexDeclarationStruct vertexDeclarations, List<string>? strings = null, Dictionary<int, int>? blendIndicesDict = null, List<Vector4>? bitangents = null ) {
            var ret = new Dictionary<string, Dictionary<int, List<byte>>>();
            for( var i = 0; i < Shapes.Count; i++ ) {
                var shapeName = Shapes[i];
                if( strings == null || strings.Contains( shapeName ) ) {
                    var accessors = _accessors[i];
                    var diff = _differentVertices[i];
                    //PluginLog.Debug( $"{shapeName} - {diff.Count}" );
                    var shapeVertexData = VertexDataBuilder.GetShapeVertexData( _parent, vertexDeclarations, accessors, diff, blendIndicesDict, bitangents  );
                    ret.Add( shapeName, shapeVertexData );
                }
            }
            return ret;
        }
    }
}
