using Lumina;
using Lumina.Data.Parsing;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Xande.GltfImporter {
    internal class ShapeBuilder {
        public readonly string ShapeName;
        public readonly List<MdlStructs.ShapeValueStruct> ShapeValues = new();
        public readonly List<int> DifferentVertices = new();
        //private VertexDataBuilder _vertexDataBuilder;
        private ILogger? _logger;
        public int VertexCount => DifferentVertices.Count;

        public ShapeBuilder( string name, MeshPrimitive primitive, int morphTargetIndex, MdlStructs.VertexDeclarationStruct vertexDeclarationStruct, ILogger? logger = null ) {
            ShapeName = name;
            //_vertexDataBuilder = new( primitive, vertexDeclarationStruct );

            var shape = primitive.GetMorphTargetAccessors( morphTargetIndex );
            //_vertexDataBuilder.AddShape( ShapeName, shape );

            shape.TryGetValue( "POSITION", out var positionsAccessor );
            var shapePositions = positionsAccessor?.AsVector3Array();

            var indices = primitive.GetIndices();

            for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                var vertexIdx = indices[indexIdx];
                if( shapePositions[( int )vertexIdx] == Vector3.Zero ) {
                    continue;
                }

                if( !DifferentVertices.Contains( ( int )vertexIdx ) ) {
                    DifferentVertices.Add( ( int )vertexIdx );
                }
                ShapeValues.Add( new() {
                    BaseIndicesIndex = ( ushort )indexIdx,
                    ReplacingVertexIndex = ( ushort )DifferentVertices.IndexOf( ( int )vertexIdx )
                } );
            }
        }
    }
}
