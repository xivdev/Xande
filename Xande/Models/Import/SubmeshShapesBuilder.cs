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

        private Mesh _mesh;
        private List<List<MdlStructs.ShapeValueStruct>> _shapeValues = new();
        private List<MdlStructs.ShapeMeshStruct> _shapeMeshes = new();
        private List<MdlStructs.ShapeStruct> _shapeStructs = new();
        private List<List<int>> _differentVertices = new();
        private List<IReadOnlyDictionary<string, Accessor>> _accessors = new();

        public SubmeshShapesBuilder( Mesh mesh ) {
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
                            _shapeMeshes.Add( new() );
                            _shapeStructs.Add( new() );
                            try {
                                var indices = primitive.GetIndices();
                                if( indices != null ) {
                                    for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                                        var index = indices[indexIdx];
                                        for( var j = 0; j < _differentVertices.Count; j++ ) {
                                            if( _differentVertices[j].Contains( ( int )index ) ) {
                                                _shapeValues[i].Add( new() {
                                                    BaseIndicesIndex = ( ushort )( indexIdx ),
                                                    // Later, we will have to add the total number of vertices (and vertices added via shapes) that come before this Submesh
                                                    ReplacingVertexIndex = ( ushort )( _differentVertices[j].IndexOf( ( int )index ) )
                                                    // That is to say, that these values are relative to the Submesh
                                                } );
                                            }
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

            for( var i = 0; i < Shapes.Count; i++ ) {
                _shapeMeshes[i] = new() {
                    MeshIndexOffset = 0, // TODO: What is MeshIndexOffset?
                    ShapeValueCount = ( uint )_shapeValues[i].Count,
                    ShapeValueOffset = 0    // Sum of all previous ShapeValueCounts
                };
            }

            for( var i = 0; i < Shapes.Count; i++ ) {
                _shapeStructs[i] = new() {
                    StringOffset = 0,   // To be filled later
                    ShapeMeshStartIndex = new ushort[] { 0, 0, 0 }, // TODO: What is ShapeMeshStartIndex?
                    ShapeMeshCount = new ushort[] { 1, 0, 0 }   // TODO: What is ShapeMeshCount?
                };
            }

            PluginLog.Debug( $"# Shapes: {Shapes.Count}" );
        }

        public int GetVertexCount( List<string>? strings = null ) {
            var vertexCount = 0;
            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = strings == null || strings.Contains( Shapes[i] );

                if( includeShape ) {
                    var hasPositions = _accessors[i].TryGetValue( "POSITION", out var positionAccessor );
                    if( hasPositions ) {
                        vertexCount += positionAccessor.Count;
                    }
                }
            }
            return vertexCount;
        }

        public List<MdlStructs.ShapeStruct> GetShapeStructs( List<string>? shapeNames = null ) {
            var ret = new List<MdlStructs.ShapeStruct>();
            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = shapeNames == null || shapeNames.Contains( Shapes[i] );

                if( includeShape ) {
                    ret.Add( _shapeStructs[i] );
                }
            }

            return ret;
        }

        public MdlStructs.ShapeStruct? GetShapeStruct(string shapeName) {
            var index = Shapes.IndexOf( shapeName );
            if (index >= 0 ) {
                return _shapeStructs[index];
            }
            else {
                return null;
            }
        }

        public MdlStructs.ShapeMeshStruct? GetShapeMeshStruct(string shapeName) {
            var index = Shapes.IndexOf( shapeName );
            if (index >=0) {
                return _shapeMeshes[index];
            }
            return null;
        }

        public List<MdlStructs.ShapeValueStruct>? GetShapeValueStructs(string shapeName) {
            var index = Shapes.IndexOf( shapeName );
            if (index >=0) {
                return _shapeValues[index];
            }
            return null;
        }

        public List<MdlStructs.ShapeMeshStruct> GetShapeMeshStructs( List<string>? shapeNames = null ) {
            var ret = new List<MdlStructs.ShapeMeshStruct>();
            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = shapeNames == null || shapeNames.Contains( Shapes[i] );

                if( includeShape ) {
                    ret.Add( _shapeMeshes[i] );
                }
            }
            return ret;
        }

        public List<MdlStructs.ShapeValueStruct> GetShapeValueStructs( List<string>? shapeNames = null ) {
            var ret = new List<MdlStructs.ShapeValueStruct>();
            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = shapeNames == null || shapeNames.Contains( Shapes[i] );

                if( includeShape ) {
                    ret.AddRange( _shapeValues[i] );
                }
            }
            return ret;
        }

        public List<IReadOnlyDictionary<string, Accessor>> GetActiveShapeAccessors( List<string>? strings = null ) {
            var ret = new List<IReadOnlyDictionary<string, Accessor>>();

            for( var i = 0; i < Shapes.Count; i++ ) {
                var includeShape = strings == null || strings.Contains( Shapes[i] );
                if( includeShape ) {
                    ret.Add( _accessors[i] );
                }
            }
            return ret;
        }
    }
}
