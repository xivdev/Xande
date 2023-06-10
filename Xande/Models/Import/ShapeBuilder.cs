using Lumina.Data.Parsing;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class ShapeBuilder {
        public readonly string ShapeName;
        public readonly List<MdlStructs.ShapeValueStruct> ShapeValues = new();
        public int VertexCount => ShapeValues.Count;
        private readonly SubmeshBuilder _submeshBuilder;

        private List<int> _differentVertices = new();
        private IReadOnlyDictionary<string, Accessor> _accessors = new Dictionary<string, Accessor>();

        public ShapeBuilder(SubmeshBuilder parent, string name, MeshPrimitive primitive, int morphTargetIndex) {
            _submeshBuilder = parent;
            ShapeName = name;
            Add(primitive, morphTargetIndex);
        }

        public void Add(MeshPrimitive primitive, int morphTargetIndex) {
            var shape = primitive.GetMorphTargetAccessors( morphTargetIndex );
            _accessors = shape;

            var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
            var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

            var shapePositions = positionsAccessor?.AsVector3Array();
            var shapeNormals = normalsAccessor?.AsVector3Array();

            // TOOD: Do we need to check shapeNormals?
            if( shapePositions != null ) {
                for( var i = 0; i < shapePositions.Count; i++ ) {
                    if( shapePositions[i] != Vector3.Zero ) {
                        _differentVertices.Add( i );
                    }
                }
                try {
                    var indices = primitive.GetIndices();
                    if( indices != null ) {
                        for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                            var index = indices[indexIdx];
                            if( _differentVertices.Contains( ( int )index ) ) {
                                ShapeValues.Add( new() {
                                    BaseIndicesIndex = ( ushort )indexIdx,
                                    ReplacingVertexIndex = ( ushort )_differentVertices.IndexOf( ( int )index )
                                } );
                            }
                        }
                    }
                }
                catch( Exception ex ) {

                }
            }
        }

        public int GetVertexCount() {
            return _differentVertices.Count;
        }

        public Dictionary<int, List<byte>> GetVertexData(MdlStructs.VertexDeclarationStruct vertexDeclaration, Dictionary<int, int>? blendIndicesDict, List<Vector4>? bitangents = null) {
            return VertexDataBuilder.GetShapeVertexData( _submeshBuilder, vertexDeclaration, _accessors, _differentVertices, blendIndicesDict, bitangents );
        }
    }
}
