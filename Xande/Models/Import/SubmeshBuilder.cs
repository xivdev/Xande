using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mesh = SharpGLTF.Schema2.Mesh;


namespace Xande.Models.Import {
    internal class SubmeshBuilder {
        public int IndexCount => Indices.Count;
        public List<uint> Indices = new List<uint>();
        public int BoneCount { get; } = 0;
        public SubmeshShapesBuilder SubmeshShapeBuilder;
        public Mesh Mesh;
        public string MaterialPath = String.Empty;
        private string _material = String.Empty;

        public Vector4 MinBoundingBox = new( 9999f, 9999f, 9999f, -9999f );
        public Vector4 MaxBoundingBox = new( -9999f, -9999f, -9999f, 9999f );

        // Does not include shape vertices
        private int _vertexCount = 0;

        private List<string> _attributes;
        private List<int> _boneIndices = new();

        public Dictionary<int, string> OriginalBoneIndexToStrings = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="skeleton"></param>
        /// <param name="indexCount">The number of (3d modeling) indices that have come before this submesh</param>
        public SubmeshBuilder( Mesh mesh, List<string> skeleton ) {
            Mesh = mesh;
            SubmeshShapeBuilder = new( Mesh );

            foreach( var primitive in Mesh.Primitives ) {

                Indices.AddRange( primitive.GetIndices() );
                var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                var material = primitive.Material.Name;

                if( String.IsNullOrEmpty( material ) ) {
                    PluginLog.Error( "Submesh had null material name" );
                }
                else {
                    if( String.IsNullOrEmpty( MaterialPath ) ) {
                        _material = material;
                        MaterialPath = AdjustMaterialPath( _material );
                    }
                    else {
                        if( material != _material || material != MaterialPath ) {
                            PluginLog.Error( $"Found more than one material name. Original: \"{MaterialPath}\" vs \"{material}\"" );
                        }
                    }
                }

                if( positions != null ) {
                    _vertexCount += positions.Count;

                    foreach( var pos in positions ) {
                        MinBoundingBox.X = MinBoundingBox.X < pos.X ? MinBoundingBox.X : pos.X;
                        MinBoundingBox.Y = MinBoundingBox.Y < pos.Y ? MinBoundingBox.Y : pos.Y;
                        MinBoundingBox.Z = MinBoundingBox.Z < pos.Z ? MinBoundingBox.Z : pos.Z;

                        MaxBoundingBox.X = MaxBoundingBox.X > pos.X ? MaxBoundingBox.X : pos.X;
                        MaxBoundingBox.Y = MaxBoundingBox.Y > pos.Y ? MaxBoundingBox.Y : pos.Y;
                        MaxBoundingBox.Z = MaxBoundingBox.Z > pos.Z ? MaxBoundingBox.Z : pos.Z;
                    }
                }
                else {
                    PluginLog.Error( "This submesh had no positions." );
                }

                if( blendIndices != null ) {
                    foreach( var blendIndex in blendIndices ) {
                        for( var i = 0; i < 4; i++ ) {
                            var index = ( int )blendIndex[i];
                            if( !OriginalBoneIndexToStrings.ContainsKey( index ) && index < skeleton.Count &&
                                skeleton[index] != "n_root" && skeleton[index] != "n_hara" ) {
                                OriginalBoneIndexToStrings.Add( index, skeleton[index] );
                            }
                        }
                    }
                }
            }

            BoneCount = OriginalBoneIndexToStrings.Keys.Count;
        }

        public List<ushort> GetSubmeshBoneMap( List<string> bones ) {
            var ret = new List<ushort>();
            foreach( var val in OriginalBoneIndexToStrings.Values ) {
                var index = bones.IndexOf( val );
                PluginLog.Debug( $"Adding to submeshbonemap: {( ushort )index}" );
                ret.Add( ( ushort )index );
            }
            return ret;
        }

        public int GetVertexCount( List<string>? strings = null ) {
            return _vertexCount; // + SubmeshShapeBuilder.GetVertexCount( strings );
        }

        public uint GetAttributeIndexMask( List<string> attributes ) {
            return 0;
        }

        public List<byte> GetIndexData( int indexCounter = 0 ) {
            var ret = new List<byte>();
            foreach( var index in Indices ) {
                ret.AddRange( BitConverter.GetBytes( ( ushort )( index + indexCounter ) ) );
            }
            return ret;
        }

        /*
        public List<byte> GetVertexData() {

        }
        */

        public static string AdjustMaterialPath( string mat ) {
            var ret = mat;
            if( !mat.StartsWith( "/" ) ) {
                ret = "/" + ret;
            }
            if( !mat.EndsWith( ".mtrl" ) ) {
                ret = ret + ".mtrl";
            }
            return ret;
        }

        public List<Vector4> CalculateTangents() {
            var tris = Mesh.EvaluateTriangles();
            var tangents = new List<Vector3>( tris.Count() * 3 );
            var bitangents = new List<Vector3>( tris.Count() * 3 );
            tangents.AddRange( Enumerable.Repeat( Vector3.Zero, tangents.Count ) );
            bitangents.AddRange( Enumerable.Repeat( Vector3.Zero, bitangents.Count ) );

            var ret = new List<Vector4>();

            foreach( var tri in tris ) {
                tri.A.GetGeometry().TryGetTangent( out var aTan );
                tri.B.GetGeometry().TryGetTangent( out var bTan );
                tri.C.GetGeometry().TryGetTangent( out var cTan );

                tri.A.GetGeometry().TryGetNormal( out var aNo );
                tri.B.GetGeometry().TryGetNormal( out var bNo );
                tri.C.GetGeometry().TryGetNormal( out var cNo );

                /*
                var vertex1Pos = tri.A.GetGeometry().GetPosition();
                var vertex2Pos = tri.B.GetGeometry().GetPosition();
                var vertex3Pos = tri.C.GetGeometry().GetPosition();

                var vertex1UV = tri.A.GetMaterial().GetTexCoord( 0 );
                var vertex2UV = tri.B.GetMaterial().GetTexCoord( 0 );
                var vertex3UV = tri.C.GetMaterial().GetTexCoord( 0 );

                var delta1 = vertex2Pos - vertex1Pos;
                var delta2 = vertex3Pos - vertex1Pos;

                var deltauv1 = vertex2UV - vertex1UV;   //uv1
                var deltauv2 = vertex3UV - vertex1UV;   //uv2

                var r = 1f / ( deltauv1.X * deltauv2.Y - deltauv2.X * deltauv1.X );
                var sdir = new Vector3((deltauv2.Y * delta1.X - deltauv1.X * delta2.X)*r, ;
                */
                var vertex1Pos = tri.A.GetGeometry().GetPosition();
                var vertex2Pos = tri.B.GetGeometry().GetPosition();
                var vertex3Pos = tri.C.GetGeometry().GetPosition();

                var vertex1UV = tri.A.GetMaterial().GetTexCoord( 0 );
                var vertex2UV = tri.B.GetMaterial().GetTexCoord( 0 );
                var vertex3UV = tri.C.GetMaterial().GetTexCoord( 0 );

                var deltaX1 = vertex2Pos.X - vertex1Pos.X;
                var deltaX2 = vertex3Pos.X - vertex1Pos.X;
                var deltaY1 = vertex2Pos.Y - vertex1Pos.Y;
                var deltaY2 = vertex3Pos.Y - vertex1Pos.Y;
                var deltaZ1 = vertex2Pos.Z - vertex1Pos.Z;
                var deltaZ2 = vertex3Pos.Z - vertex1Pos.Z;

                var deltaU1 = vertex2UV.X - vertex1UV.X;
                var deltaU2 = vertex3UV.X - vertex1UV.X;
                var deltaV1 = vertex2UV.Y - vertex1UV.Y;
                var deltaV2 = vertex3UV.Y - vertex1UV.Y;

                var r = 1.0f / ( deltaU1 * deltaV2 - deltaU2 * deltaV1 );
                var sdir = new Vector3( ( deltaV2 * deltaX1 - deltaV1 * deltaX2 ) * r, ( deltaV2 * deltaY1 - deltaV1 * deltaY2 ) * r, ( deltaV2 * deltaZ1 - deltaV1 * deltaZ2 ) * r );
                var tdir = new Vector3( ( deltaU1 * deltaX2 - deltaU2 * deltaX1 ) * r, ( deltaU1 * deltaY2 - deltaU2 * deltaY1 ) * r, ( deltaU1 * deltaZ2 - deltaU2 * deltaZ1 ) * r );

                tri.A.GetGeometry().TryGetNormal( out var n1 );
                tri.B.GetGeometry().TryGetNormal( out var n2 );
                tri.C.GetGeometry().TryGetNormal( out var n3 );
                var t = sdir;
                var b = tdir;

                var narr = new List<Vector3>() { n1, n2, n3 };
                foreach( var n in narr ) {
                    var tangent = t - ( n * Vector3.Dot( n, t ) );
                    tangent = Vector3.Normalize( tangent );

                    var binormal = Vector3.Cross( n, tangent );
                    binormal = Vector3.Normalize( binormal );

                    var handedness = Vector3.Dot( Vector3.Cross( t, b ), n ) > 0 ? 1 : -1;
                    binormal *= handedness;

                    //PluginLog.Debug( $"tan: {tangent}" );
                    //PluginLog.Debug( $"bi: {binormal}" );
                    var val = new Vector4( binormal, handedness );
                    ret.Add( val );
                }
            }
            return ret;
        }
    }
}
