using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Xande.GltfImporter {
    internal class MeshBuilder {
        public List<SubmeshBuilder> Submeshes = new();
        public Dictionary<int, List<byte>> VertexData = new();
        public SortedSet<string> Attributes = new();
        public MdlStructs.BoneTableStruct BoneTableStruct;
        public List<string> Bones => _originalBoneIndexToString.Values.ToList();
        public string Material = String.Empty;
        public List<string> Shapes = new();
        private readonly ILogger? _logger;

        private Dictionary<int, string> _originalBoneIndexToString = new();
        private Dictionary<int, int> _blendIndicesDict = new();

        public uint IndexCount { get; protected set; } = 0;

        public MeshBuilder( List<SubmeshBuilder> submeshes, ILogger? logger = null) {
            _logger = logger;
            foreach( var sm in submeshes ) {
                Submeshes.Add( sm );
                TryAddBones( sm );
                AddShapes( sm );
                AddSubmeshAttributes( sm );

                IndexCount += sm.IndexCount;

                if( String.IsNullOrEmpty( Material ) ) {
                    Material = sm.MaterialPath;
                }
                else {
                    if( Material != sm.MaterialPath ) {
                        _logger?.Error( $"Found multiple materials. Original \"{Material}\" vs \"{sm.MaterialPath}\"" );
                    }
                }
            }

            if( Bones.Count == 0 ) {
                _logger?.Warning( $" Mesh had zero bones. This can cause a game crash if a skeleton is expected." );
            }
        }

        public int GetVertexCount() {
            return GetVertexCount( false );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="strings">An optional list of strings that may or may not contain the names of used shapes</param>
        /// <returns></returns>
        public int GetVertexCount( bool includeShapes, List<string>? strings = null ) {
            var vertexCount = 0;
            foreach( var submeshBuilder in Submeshes ) {
                vertexCount += submeshBuilder.GetVertexCount( includeShapes, strings );
            }
            return vertexCount;
        }

        public int GetMaterialIndex( List<string> materials ) {
            return materials.IndexOf( Material );
        }

        public MdlStructs.BoneTableStruct GetBoneTableStruct( List<string> bones, List<string> hierarchyBones ) {
            _blendIndicesDict = new();
            var boneTable = new List<ushort>();
            var values = _originalBoneIndexToString.Values.Where( x => x != "n_root" ).ToList();
            var newValues = new List<string>();
            foreach( var b in hierarchyBones ) {
                if( values.Contains( b ) ) {
                    newValues.Add( b );
                }
            }

            foreach( var v in _originalBoneIndexToString ) {
                var value = newValues.IndexOf( v.Value );
                if( value != -1 ) {
                    _blendIndicesDict.Add( v.Key, value );
                }
            }

            foreach( var v in newValues ) {
                var index = bones.IndexOf( v );
                if( index >= 0 ) {
                    boneTable.Add( ( ushort )index );
                }
            }

            var boneCount = boneTable.Count;

            while( boneTable.Count < 64 ) {
                boneTable.Add( 0 );
            }

            foreach( var sm in Submeshes ) {
                sm.SetBlendIndicesDict( _blendIndicesDict );
            }

            return new() {
                BoneIndex = boneTable.ToArray(),
                BoneCount = ( byte )boneCount
            };
        }

        public List<ushort> GetSubmeshBoneMap( List<string> bones ) {
            var ret = new List<ushort>();
            foreach( var b in _originalBoneIndexToString.Values ) {
                var index = bones.IndexOf( b );
                ret.Add( ( ushort )index );
            }
            return ret;
        }

        public Dictionary<int, List<byte>> GetVertexData() {
            var vertexDict = new Dictionary<int, List<byte>>();
            foreach( var submesh in Submeshes ) {
                var submeshVertexData = submesh.GetVertexData();

                foreach( var stream in submeshVertexData.Keys ) {
                    if( !vertexDict.ContainsKey( stream ) ) {
                        vertexDict.Add( stream, new() );
                    }
                    vertexDict[stream].AddRange( submeshVertexData[stream] );
                }
            }
            return vertexDict;
        }

        public async Task<Dictionary<int, List<byte>>> GetVertexDataAsync() {
            var vertexDict = new Dictionary<int, List<byte >> ();
            var tasks = new Task<Dictionary<int, List<byte>>>[ Submeshes.Count ];
            for (var i = 0; i < Submeshes.Count; i++) {
                var j = i;
                tasks[j] = Task.Run( () => Task.FromResult( Submeshes[j].GetVertexData() ) );
            }

            Task.WaitAll( tasks );

            for (var i = 0; i < Submeshes.Count; i++ ) {
                var t = await tasks[i];
                foreach (var stream in t.Keys ) {
                    if (!vertexDict.ContainsKey(stream)) {
                        vertexDict.Add( stream, new() );

                    }
                    vertexDict[stream].AddRange( t[stream] );

                }
            }

            return vertexDict;
        }

        private void TryAddBones( SubmeshBuilder submesh ) {
            foreach( var kvp in submesh.OriginalBoneIndexToStrings ) {
                if( !_originalBoneIndexToString.ContainsKey( kvp.Key ) ) {
                    _originalBoneIndexToString.Add( kvp.Key, kvp.Value );
                }
            }
            if( _originalBoneIndexToString.Keys.Count > 64 ) {
                _logger?.Error( $"There are currently {_originalBoneIndexToString.Keys.Count} bones, which is over the allowed 64." );
            }
        }

        private void AddShapes( SubmeshBuilder submesh ) {
            foreach( var s in submesh.Shapes ) {
                if( !Shapes.Contains( s ) ) {
                    Shapes.Add( s );
                }
            }
        }

        private void AddSubmeshAttributes( SubmeshBuilder submesh ) {
            foreach( var attr in submesh.Attributes ) {
                AddAttribute( attr );
            }
        }

        public bool AddAttribute( string s ) {
            if( !Attributes.Contains( s ) ) {
                Attributes.Add( s );
                return true;
            }
            return false;
        }
    }
}
