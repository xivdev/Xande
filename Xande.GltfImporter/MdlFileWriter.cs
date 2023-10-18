using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xande.GltfImporter {
    public class MdlFileWriter : IDisposable {
        private MdlFile _file;
        private BinaryWriter _w;
        private ILogger? _logger;

        public MdlFileWriter( MdlFile file, Stream stream, ILogger? logger = null ) {
            _logger = logger;
            _file = file;
            _w = new BinaryWriter( stream );
        }

        public void WriteAll( IEnumerable<byte> vertexData, IEnumerable<byte> indexData ) {
            WriteFileHeader( _file.FileHeader );
            WriteVertexDeclarations( _file.VertexDeclarations );

            _w.Write( _file.StringCount );
            _w.Write( ( ushort )0 );
            _w.Write( ( uint )_file.Strings.Length );
            _w.Write( _file.Strings );

            WriteModelHeader( _file.ModelHeader );
            WriteElementIds( _file.ElementIds );
            WriteLods( _file.Lods );

            if( _file.ModelHeader.ExtraLodEnabled ) {
                WriteExtraLods( _file.ExtraLods );
            }

            WriteMeshStructs( _file.Meshes );

            for( var i = 0; i < _file.AttributeNameOffsets.Length; i++ ) {
                _w.Write( _file.AttributeNameOffsets[i] );
            }
            WriteTerrainShadowMeshes( _file.TerrainShadowMeshes );
            WriteSubmeshStructs( _file.Submeshes );
            WriteTerrainShadowSubmeshes( _file.TerrainShadowSubmeshes );

            for( var i = 0; i < _file.MaterialNameOffsets.Length; i++ ) {
                _w.Write( _file.MaterialNameOffsets[i] );
            }

            for( var i = 0; i < _file.BoneNameOffsets.Length; i++ ) {
                _w.Write( _file.BoneNameOffsets[i] );
            }

            WriteBoneTableStructs( _file.BoneTables );
            WriteShapeStructs( _file.Shapes );
            WriteShapeMeshStructs( _file.ShapeMeshes );
            WriteShapeValueStructs( _file.ShapeValues );
            var submeshBoneMapSize = _file.SubmeshBoneMap.Length * 2;
            _w.Write( ( uint )submeshBoneMapSize );
            foreach( var val in _file.SubmeshBoneMap ) {
                _w.Write( val );
            };

            _w.Write( ( byte )7 );
            _w.Seek( 7, SeekOrigin.Current );

            WriteBoundingBoxStructs( _file.BoundingBoxes );
            WriteBoundingBoxStructs( _file.ModelBoundingBoxes );
            WriteBoundingBoxStructs( _file.WaterBoundingBoxes );
            WriteBoundingBoxStructs( _file.VerticalFogBoundingBoxes );
            foreach( var boneBoundingBox in _file.BoneBoundingBoxes ) {
                WriteBoundingBoxStructs( boneBoundingBox );
            }

            _w.Write( vertexData.ToArray() );
            _w.Write( indexData.ToArray() );

            _logger?.Debug( "Finished writing" );
        }

        private void WriteFileHeader( MdlStructs.ModelFileHeader modelFileHeader ) {
            _w.Write( modelFileHeader.Version );
            _w.Write( modelFileHeader.StackSize );
            _w.Write( modelFileHeader.RuntimeSize );
            _w.Write( modelFileHeader.VertexDeclarationCount );
            _w.Write( modelFileHeader.MaterialCount );
            for( var i = 0; i < 3; i++ ) {
                _w.Write( modelFileHeader.VertexOffset[i] );
            }
            for( var i = 0; i < 3; i++ ) {
                _w.Write( modelFileHeader.IndexOffset[i] );
            }
            for( var i = 0; i < 3; i++ ) {
                _w.Write( modelFileHeader.VertexBufferSize[i] );
            }
            for( var i = 0; i < 3; i++ ) {
                _w.Write( modelFileHeader.IndexBufferSize[i] );
            }
            _w.Write( modelFileHeader.LodCount );
            _w.Write( modelFileHeader.EnableIndexBufferStreaming );
            _w.Write( modelFileHeader.EnableEdgeGeometry );
            _w.Write( ( byte )0 );
        }

        private void WriteVertexDeclarations( MdlStructs.VertexDeclarationStruct[] declarations ) {
            foreach( var declaration in declarations ) {
                foreach( var vertexElement in declaration.VertexElements ) {
                    _w.Write( vertexElement.Stream );
                    _w.Write( vertexElement.Offset );
                    _w.Write( vertexElement.Type );
                    _w.Write( vertexElement.Usage );
                    _w.Write( vertexElement.UsageIndex );
                    _w.Seek( 3, SeekOrigin.Current );
                }
            }
        }

        private void WriteModelHeader( MdlStructs.ModelHeader modelHeader ) {
            _w.Write( modelHeader.Radius );
            _w.Write( modelHeader.MeshCount );
            _w.Write( modelHeader.AttributeCount );
            _w.Write( modelHeader.SubmeshCount );
            _w.Write( modelHeader.MaterialCount );
            _w.Write( modelHeader.BoneCount );
            _w.Write( modelHeader.BoneTableCount );
            _w.Write( modelHeader.ShapeCount );
            _w.Write( modelHeader.ShapeMeshCount );
            _w.Write( modelHeader.ShapeValueCount );
            _w.Write( modelHeader.LodCount );

            // Flags are private, so we need to do this - ugly
            var flags1 = new BitArray( new bool[]
                {
                modelHeader.DustOcclusionEnabled,
                modelHeader.SnowOcclusionEnabled,
                modelHeader.RainOcclusionEnabled,
                modelHeader.Unknown1,
                modelHeader.BgLightingReflectionEnabled,
                //modelHeader.WavingAnimationDisabled,
                true,
                modelHeader.LightShadowDisabled,
                modelHeader.ShadowDisabled
                }.Reverse().ToArray()
                );

            var flags1Byte = new byte[1];
            flags1.CopyTo( flags1Byte, 0 );
            _w.Write( flags1Byte[0] );

            _w.Write( modelHeader.ElementIdCount );
            _w.Write( modelHeader.TerrainShadowMeshCount );

            var flags2 = new BitArray( new bool[] {
            modelHeader.Unknown2,
            modelHeader.BgUvScrollEnabled,
            modelHeader.EnableForceNonResident,
            modelHeader.ExtraLodEnabled,
            modelHeader.ShadowMaskEnabled,
            modelHeader.ForceLodRangeEnabled,
            modelHeader.EdgeGeometryEnabled,
            modelHeader.Unknown3
        } );
            var flags2Byte = new byte[1];
            flags2.CopyTo( flags2Byte, 0 );
            _w.Write( flags2Byte[0] );

            _w.Write( modelHeader.ModelClipOutDistance );
            _w.Write( modelHeader.ShadowClipOutDistance );
            _w.Write( modelHeader.Unknown4 );
            _w.Write( modelHeader.TerrainShadowSubmeshCount );
            _w.Write( ( byte )0 ); // ??? why is that private in lumina
            _w.Write( modelHeader.BGChangeMaterialIndex );
            _w.Write( modelHeader.BGCrestChangeMaterialIndex );
            _w.Write( modelHeader.Unknown6 );
            _w.Write( modelHeader.Unknown7 );
            _w.Write( modelHeader.Unknown8 );
            _w.Write( modelHeader.Unknown9 );
            _w.Seek( 6, SeekOrigin.Current );
        }

        private void WriteElementIds( MdlStructs.ElementIdStruct[] elements ) {
            foreach( var e in elements ) {
                _w.Write( e.ElementId );
                _w.Write( e.ParentBoneName );
                for( var i = 0; i < 3; i++ ) {
                    _w.Write( e.Translate[i] );
                }
                for( var i = 0; i < 3; i++ ) {
                    _w.Write( e.Rotate[i] );
                }
            }
        }

        private void WriteLods( MdlStructs.LodStruct[] lods ) {
            for( var i = 0; i < 3; i++ ) {
                var lod = lods[i];

                _w.Write( lod.MeshIndex );
                _w.Write( lod.MeshCount );
                _w.Write( lod.ModelLodRange );
                _w.Write( lod.TextureLodRange );
                _w.Write( lod.WaterMeshIndex );
                _w.Write( lod.WaterMeshCount );
                _w.Write( lod.ShadowMeshIndex );
                _w.Write( lod.ShadowMeshCount );
                _w.Write( lod.TerrainShadowMeshIndex );
                _w.Write( lod.TerrainShadowMeshCount );
                _w.Write( lod.VerticalFogMeshIndex );
                _w.Write( lod.VerticalFogMeshCount );

                _w.Write( lod.EdgeGeometrySize );
                _w.Write( lod.EdgeGeometryDataOffset );
                _w.Write( lod.PolygonCount );
                _w.Write( lod.Unknown1 );
                _w.Write( lod.VertexBufferSize );
                _w.Write( lod.IndexBufferSize );
                _w.Write( lod.VertexDataOffset );
                _w.Write( lod.IndexDataOffset );
            }
        }

        private void WriteExtraLods( MdlStructs.ExtraLodStruct[] lods ) {
            foreach( var lod in lods ) {
                _w.Write( lod.LightShaftMeshIndex );
                _w.Write( lod.LightShaftMeshCount );
                _w.Write( lod.GlassMeshIndex );
                _w.Write( lod.GlassMeshCount );
                _w.Write( lod.MaterialChangeMeshIndex );
                _w.Write( lod.MaterialChangeMeshCount );
                _w.Write( lod.CrestChangeMeshIndex );
                _w.Write( lod.CrestChangeMeshCount );
                _w.Write( lod.Unknown1 );
                _w.Write( lod.Unknown2 );
                _w.Write( lod.Unknown3 );
                _w.Write( lod.Unknown4 );
                _w.Write( lod.Unknown5 );
                _w.Write( lod.Unknown6 );
                _w.Write( lod.Unknown7 );
                _w.Write( lod.Unknown8 );
                _w.Write( lod.Unknown9 );
                _w.Write( lod.Unknown10 );
                _w.Write( lod.Unknown11 );
                _w.Write( lod.Unknown12 );
            }
        }

        private void WriteMeshStructs( MdlStructs.MeshStruct[] meshes ) {
            foreach( var mesh in meshes ) {
                _w.Write( mesh.VertexCount );
                _w.Write( ( ushort )0 );   // mesh.Padding
                _w.Write( mesh.IndexCount );
                _w.Write( mesh.MaterialIndex );
                _w.Write( mesh.SubMeshIndex );
                _w.Write( mesh.SubMeshCount );
                _w.Write( mesh.BoneTableIndex );
                _w.Write( mesh.StartIndex );
                for( var i = 0; i < 3; i++ ) {
                    _w.Write( mesh.VertexBufferOffset[i] );
                }
                _w.Write( mesh.VertexBufferStride );
                _w.Write( mesh.VertexStreamCount );
            }
        }

        private void WriteTerrainShadowMeshes( MdlStructs.TerrainShadowMeshStruct[] meshes ) {
            foreach( var mesh in meshes ) {
                _w.Write( mesh.IndexCount );
                _w.Write( mesh.StartIndex );
                _w.Write( mesh.VertexBufferOffset );
                _w.Write( mesh.VertexCount );
                _w.Write( mesh.SubMeshIndex );
                _w.Write( mesh.SubMeshCount );
                _w.Write( mesh.VertexBufferStride );
                _w.Write( ( byte )0 );  // Padding
            }
        }

        private void WriteSubmeshStructs( MdlStructs.SubmeshStruct[] submeshes ) {
            foreach( var submesh in submeshes ) {
                _w.Write( submesh.IndexOffset );
                _w.Write( submesh.IndexCount );
                _w.Write( submesh.AttributeIndexMask );
                _w.Write( submesh.BoneStartIndex );
                _w.Write( submesh.BoneCount );
            }
        }

        private void WriteTerrainShadowSubmeshes( MdlStructs.TerrainShadowSubmeshStruct[] submeshes ) {
            foreach( var submesh in submeshes ) {
                _w.Write( submesh.IndexOffset );
                _w.Write( submesh.IndexCount );
                _w.Write( submesh.Unknown1 );
                _w.Write( submesh.Unknown2 );
            }
        }

        private void WriteBoneTableStructs( MdlStructs.BoneTableStruct[] bonetables ) {
            foreach( var boneTable in bonetables ) {
                for( var i = 0; i < 64; i++ ) {
                    _w.Write( boneTable.BoneIndex[i] );
                }
                _w.Write( boneTable.BoneCount );
                _w.Seek( 3, SeekOrigin.Current );
            }
        }

        private void WriteShapeStructs( MdlStructs.ShapeStruct[] shapeStructs ) {
            foreach( var shapeStruct in shapeStructs ) {
                _w.Write( shapeStruct.StringOffset );
                for( var i = 0; i < 3; i++ ) {
                    _w.Write( shapeStruct.ShapeMeshStartIndex[i] );
                }
                for( var i = 0; i < 3; i++ ) {
                    _w.Write( shapeStruct.ShapeMeshCount[i] );
                }
            }
        }

        private void WriteShapeMeshStructs( MdlStructs.ShapeMeshStruct[] shapeMeshStructs ) {
            foreach( var shapeMeshStruct in shapeMeshStructs ) {
                _w.Write( shapeMeshStruct.MeshIndexOffset );
                _w.Write( shapeMeshStruct.ShapeValueCount );
                _w.Write( shapeMeshStruct.ShapeValueOffset );
            }
        }

        private void WriteShapeValueStructs( MdlStructs.ShapeValueStruct[] shapeValueStructs ) {
            foreach( var shapeValueStruct in shapeValueStructs ) {
                _w.Write( shapeValueStruct.BaseIndicesIndex );
                _w.Write( shapeValueStruct.ReplacingVertexIndex );
            }
        }

        private void WriteBoundingBoxStructs( MdlStructs.BoundingBoxStruct bb ) {
            for( var i = 0; i < 4; i++ ) {
                _w.Write( bb.Min[i] );
            }
            for( var i = 0; i < 4; i++ ) {
                _w.Write( bb.Max[i] );
            }
        }

        public void Dispose() {
            _w.Dispose();
        }
    }
}
