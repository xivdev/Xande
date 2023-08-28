using Dalamud.Logging;
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
        public readonly SubmeshBuilder SubmeshBuilder;

        private List<int> _differentVertices = new();

        private VertexDataBuilder _vertexDataBuilder;

        public ShapeBuilder( SubmeshBuilder parent, string name, MeshPrimitive primitive, int morphTargetIndex, MdlStructs.VertexDeclarationStruct vertexDeclarationStruct ) {
            PluginLog.Debug($"SHAPE: {name} - {morphTargetIndex}");
            SubmeshBuilder = parent;
            ShapeName = name;
            _vertexDataBuilder = new( primitive, vertexDeclarationStruct );

            var shape = primitive.GetMorphTargetAccessors( morphTargetIndex );
            _vertexDataBuilder.AddShape( ShapeName, shape );

            var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
            var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

            var shapePositions = positionsAccessor?.AsVector3Array();
            var shapeNormals = normalsAccessor?.AsVector3Array();

            // TOOD: Do we need to check shapeNormals?
            var indices = primitive.GetIndices();
            PluginLog.Debug( $"Shape {name} indices size: {indices.Count}" );
            if( indices != null ) {
                for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                    var vertexIdx = indices[indexIdx];
                    if( shapePositions[( int )vertexIdx] == Vector3.Zero ) {
                        continue;
                    }

                    if( !_differentVertices.Contains( ( int )vertexIdx ) ) {
                        _differentVertices.Add( ( int )vertexIdx );
                    }
                    ShapeValues.Add( new() {
                        BaseIndicesIndex = ( ushort )indexIdx,
                        ReplacingVertexIndex = ( ushort )_differentVertices.IndexOf( ( int )vertexIdx )
                    } );
                }
                PluginLog.Debug( $"Shape {name} size = {_differentVertices.Count}" );
            }
            else {
                PluginLog.Error( $"Shape {name} had no indices." );
            }
        }

        public void SetBlendIndicesDict(Dictionary<int, int> dict) {
            _vertexDataBuilder.BlendIndicesDict = dict;
        }

        public int GetVertexCount() {
            return _differentVertices.Count;
        }

        public Dictionary<int, List<byte>> GetVertexData() {
            return _vertexDataBuilder.GetShapeVertexData( _differentVertices, ShapeName );
        }
    }
}
