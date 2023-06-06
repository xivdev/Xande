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
                        if (material != _material || material != MaterialPath) {
                            PluginLog.Error( $"Found more than one material name. Original: \"{MaterialPath}\" vs \"{material}\"" );
                        }
                    }
                }

                if (positions != null) {
                    _vertexCount += positions.Count;

                    foreach (var pos in positions) {
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
                                OriginalBoneIndexToStrings.Add(index, skeleton[index]);
                            }
                        }
                    }
                }
            }

            BoneCount = OriginalBoneIndexToStrings.Keys.Count;
        }

        public int GetVertexCount(List<string>? strings = null) {
            return _vertexCount + SubmeshShapeBuilder.GetVertexCount( strings );
        }

        public uint GetAttributeIndexMask(List<string> attributes) {
            return 0;
        }

        public List<byte> GetIndexData(int indexCounter) {
            var ret = new List<byte>();
            foreach (var index in Indices) {
                ret.AddRange(BitConverter.GetBytes((ushort)(index + indexCounter)));
            }
            return ret;
        }

        public static string AdjustMaterialPath(string mat) {
            var ret = mat;
            if (!mat.StartsWith("/")) {
                ret = "/" + ret;
            }
            if (!mat.EndsWith(".mtrl")) {
                ret = ret + ".mtrl";
            }
            return ret;
        }
    }
}
