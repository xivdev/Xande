using Dalamud.Logging;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mesh = SharpGLTF.Schema2.Mesh;

namespace Xande.Models.Import {
    internal class MdlFileMeshBuilder {
        public List<SubmeshBuilder> Submeshes = new();
        public List<string> Bones => _originalBoneIndexToString.Values.ToList();
        public string Material = String.Empty;
        public List<string> Shapes = new();

        private List<string> _skeleton;
        private string _material = String.Empty;

        private Dictionary<int, string> _originalBoneIndexToString = new();

        public int IndexCount { get; protected set; } = 0;

        public MdlFileMeshBuilder( List<string> skeleton ) {
            _skeleton = skeleton;
        }

        public void AddSubmesh( Mesh mesh ) {
            var submeshBuilder = new SubmeshBuilder( mesh, _skeleton );
            Submeshes.Add( submeshBuilder );
            TryAddBones( submeshBuilder );
            AddShapes( submeshBuilder );

            IndexCount += submeshBuilder.IndexCount;
            if (String.IsNullOrEmpty(Material)) {
                Material = submeshBuilder.MaterialPath;
            }
            else {
                if (Material != submeshBuilder.MaterialPath) {
                    PluginLog.Error( $"Found multiple materials. Original \"{Material}\" vs \"{submeshBuilder.MaterialPath}\"" );
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="strings">An optional list of strings that may or may not contain the names of used shapes</param>
        /// <returns></returns>
        public int GetVertexCount( List<string>? strings = null ) {
            var vertexCount = 0;
            foreach( var submeshBuilder in Submeshes ) {
                vertexCount += submeshBuilder.GetVertexCount( strings );
            }
            return vertexCount;
        }

        public int GetMaterialIndex( List<string> materials ) {
            PluginLog.Debug( $"Searching materials for \"{Material}\"" );
            return materials.IndexOf( Material );
        }

        public List<byte> GetVertexData( MdlStructs.VertexDeclarationStruct vds, List<string>? strings = null ) {
            var ret = new List<byte>();
            foreach( var submesh in Submeshes ) {
                ret.AddRange( GetVertexData( vds, strings ) );
            }
            return ret;
        }

        public Dictionary<int, int> GetBlendIndicesDict( List<string> bones ) {
            var ret = new Dictionary<int, int>();
            foreach( var kvp in _originalBoneIndexToString ) {
                var index = bones.IndexOf( kvp.Value );
                if (index != -1 ) {
                    ret.Add( kvp.Key, index );
                }
            }
            return ret;
        }

        public MdlStructs.BoneTableStruct GetBoneTableStruct( List<string> bones ) {
            var boneTable = new List<ushort>();
            var values = _originalBoneIndexToString.Values.Where( x => x != "n_root" ).ToList();
            foreach( var bone in bones ) {
                var index = values.IndexOf( bone );
                if( index >= 0 ) {
                    boneTable.Add( ( ushort )index );
                }
            }
            var boneCount = boneTable.Count;

            while( boneTable.Count < 64 ) {
                boneTable.Add( 0 );
            }

            return new() {
                BoneIndex = boneTable.ToArray(),
                BoneCount = ( byte )boneCount
            };
        }

        private void TryAddBones( SubmeshBuilder submesh ) {
            foreach( var kvp in submesh.OriginalBoneIndexToStrings ) {
                if( !_originalBoneIndexToString.ContainsKey( kvp.Key ) ) {
                    _originalBoneIndexToString.Add( kvp.Key, kvp.Value );
                }
            }
            if( _originalBoneIndexToString.Keys.Count > 64 ) {
                PluginLog.Error( $"There are currently {_originalBoneIndexToString.Keys.Count} bones, which is over the allowed 64." );
            }
        }

        private void AddShapes(SubmeshBuilder submesh) {
            foreach (var s in submesh.SubmeshShapeBuilder.Shapes) {
                if (!Shapes.Contains(s)) {
                    Shapes.Add( s );
                }
            }
        }
    }
}
