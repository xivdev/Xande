using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.IO;
using SharpGLTF.Materials;

namespace Xande;

public class ShapeBuilder {
    public ShapeBuilder( IReadOnlyList<Shape> shapes, IMeshBuilder< MaterialBuilder > builder ) {
        for( var i = 0; i < shapes.Count; ++i ) {
            var shape = shapes[i];
            var morph       = builder.UseMorphTarget( i );
            morph.SetVertexDelta( morph.Positions.First(), new VertexGeometryDelta( Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ ) );
        }

        var dict = new Dictionary< string, object >() {
            [ "targetNames" ] = shapes.Select( s => s.Name ).ToArray(),
        };
        builder.Extras = JsonContent.CreateFrom( dict );
    }
}