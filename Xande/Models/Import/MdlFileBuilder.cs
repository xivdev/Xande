using Dalamud.Logging;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System.Collections;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mesh = SharpGLTF.Schema2.Mesh;

namespace Xande.Models.Import;

// https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs
// https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Files/MdlFile.cs
public class MdlFileBuilder {
    private ModelRoot _root;
    private Model _origModel;

    private Dictionary<int, Dictionary<int, Mesh>> _meshes = new();
    private StringTableBuilder _stringTableBuilder;
    private List<LuminaMeshBuilder> _meshBuilders = new();

    private Dictionary<(int meshIndex, int submeshIndex, string shapeName), List<MdlStructs.ShapeValueStruct>> _shapeData = new();

    public MdlFileBuilder( ModelRoot root, Model model ) {
        _root = root;
        _origModel = model;

        _stringTableBuilder = new StringTableBuilder( _root );

        foreach( var mesh in root.LogicalMeshes ) {
            var name = mesh.Name;
            // TODO: Consider MeshIndex > 9 ?
            // TODO: What to do if it already exists
            // TODO: What to do if match does not exist - skip probably.
            var match = Regex.Match( name, @"([0-9]\.[0-9])" );

            if( match.Success ) {
                var str = match.Groups[1].Value;

                var isSubmesh = str.Contains( '.' );
                if( isSubmesh ) {
                    var parts = str.Split( '.' );
                    var meshIdx = int.Parse( parts[0] );
                    var submeshIdx = int.Parse( parts[1] );
                    if( !_meshes.ContainsKey( meshIdx ) ) {
                        _meshes[meshIdx] = new();
                    }

                    _meshes[meshIdx][submeshIdx] = mesh;
                }
                else {
                    var meshIdx = int.Parse( str );

                    if( !_meshes.ContainsKey( meshIdx ) ) _meshes[meshIdx] = new();
                    _meshes[meshIdx][-1] = mesh;
                }
            }
        }


    }

    private List<string> GetJoints( List<Node> children, List<string> matches ) {
        List<string> ret = new();
        if( children != null && children.Count() > 0 ) {
            for( int i = 0; i < children.Count(); i++ ) {
                if( matches.Contains( children[i].Name ) ) {
                    ret.Add( children[i].Name );
                }
                ret.AddRange( GetJoints( children[i].VisualChildren.ToList(), matches ) );
            }
        }

        return ret;
    }

    public int CompareShapeValueStructs( MdlStructs.ShapeValueStruct a, MdlStructs.ShapeValueStruct b ) {
        var replacingCompare = a.ReplacingVertexIndex.CompareTo( b.ReplacingVertexIndex );
        if( replacingCompare == 0 ) {
            return a.BaseIndicesIndex.CompareTo( b.BaseIndicesIndex );
        }
        else {
            return replacingCompare;
        }
    }

    private MdlStructs.VertexDeclarationStruct[] GetVertexDeclarationStructs( int size, MdlStructs.VertexDeclarationStruct vds ) {
        var ret = new List<MdlStructs.VertexDeclarationStruct>();
        /*
        for( var i = 0; i < size; i++ ) {
            var ve = new List<MdlStructs.VertexElement>();
            for( var j = 0; j < vds.VertexElements.Length; j++ ) {
                ve.Add( vds.VertexElements[j] );
            }
            if( ve.Last().Stream != 255 ) {
                ve.Add( new() {
                    Stream = 255
                } );
            }
            while( ve.Count < 17 ) {
                ve.Add( new() );
            }
            var dec = new MdlStructs.VertexDeclarationStruct() {
                VertexElements = ve.ToArray()
            };
            ret.Add( dec );
        }
        */

        var declaration = new MdlStructs.VertexDeclarationStruct();
        declaration.VertexElements = new MdlStructs.VertexElement[17];
        declaration.VertexElements[0] = new() { Stream = 0, Offset = 0, Usage = ( byte )Vertex.VertexUsage.Position, Type = ( byte )Vertex.VertexType.Single3, UsageIndex = 0 };
        declaration.VertexElements[1] = new() { Stream = 0, Offset = 12, Usage = ( byte )Vertex.VertexUsage.BlendWeights, Type = ( byte )Vertex.VertexType.ByteFloat4, UsageIndex = 0 };
        declaration.VertexElements[2] = new() { Stream = 0, Offset = 16, Usage = ( byte )Vertex.VertexUsage.BlendIndices, Type = ( byte )Vertex.VertexType.UInt, UsageIndex = 0 };

        declaration.VertexElements[3] = new() { Stream = 1, Offset = 0, Usage = ( byte )Vertex.VertexUsage.Normal, Type = ( byte )Vertex.VertexType.Single3, UsageIndex = 0 };
        declaration.VertexElements[4] = new() { Stream = 1, Offset = 12, Usage = ( byte )Vertex.VertexUsage.Tangent1, Type = ( byte )Vertex.VertexType.ByteFloat4, UsageIndex = 0 };
        declaration.VertexElements[5] = new() { Stream = 1, Offset = 16, Usage = ( byte )Vertex.VertexUsage.Color, Type = ( byte )Vertex.VertexType.ByteFloat4, UsageIndex = 0 };
        declaration.VertexElements[6] = new() { Stream = 1, Offset = 20, Usage = ( byte )Vertex.VertexUsage.UV, Type = ( byte )Vertex.VertexType.Single4, UsageIndex = 0 };
        declaration.VertexElements[7] = new() { Stream = 255, Offset = 0, Usage = 0, Type = 0, UsageIndex = 0 };
        for( var i = 8; i < 17; i++ ) {
            declaration.VertexElements[i] = new() { Stream = 0, Offset = 0, Usage = 0, Type = 0, UsageIndex = 0 };
        }
        for( int i = 0; i < size; i++ ) {
            ret.Add( declaration );
        }
        return ret.ToArray();
    }

    public (MdlFile? file, List<byte> vertexData, List<byte> indexData) Build() {
        var vertexDeclarations = GetVertexDeclarationStructs( _meshes.Keys.Count, _origModel.File.VertexDeclarations[0] );

        var allBones = new List<string>();
        var bonesToNodes = new Dictionary<string, Node>();
        var skeleton = _root.LogicalSkins?[0];
        if( skeleton != null ) {
            for( var id = 0; id < skeleton.JointsCount; id++ ) {
                var (Joint, InverseBindMatrix) = skeleton.GetJoint( id );

                var boneString = Joint.Name;
                if( !String.IsNullOrEmpty( boneString ) ) {
                    allBones.Add( boneString );
                    bonesToNodes.Add( boneString, Joint );
                }
            }
        }

        foreach( var meshIdx in _meshes.Keys ) {
            var meshBuilder = new LuminaMeshBuilder( allBones, vertexDeclarations[meshIdx] );
            foreach( var submeshIndex in _meshes[meshIdx].Keys ) {
                var submesh = _meshes[meshIdx][submeshIndex];
                meshBuilder.AddSubmesh( submesh );
            }
            _meshBuilders.Add( meshBuilder );
            _stringTableBuilder.AddBones( meshBuilder.Bones );
            _stringTableBuilder.AddMaterial( meshBuilder.Material );
            _stringTableBuilder.AddShapes( meshBuilder.Shapes );
            _stringTableBuilder.AddAttributes( meshBuilder.Attributes );
        }

        _stringTableBuilder.HierarchyBones = GetJoints( skeleton.GetJoint( 0 ).Joint.VisualChildren.ToList(), _stringTableBuilder.Bones.ToList() );

        var strings = _stringTableBuilder.GetStrings();

        var vertexData = new List<byte>();
        var vertexDict = new Dictionary<int, List<byte>>();
        var indexData = new List<byte>();


        var submeshCounter = 0;
        var vertexCounter = 0;
        var boneCounter = 0;

        var meshStructs = new List<MdlStructs.MeshStruct>();
        var submeshStructs = new List<MdlStructs.SubmeshStruct>();
        var shapeStructs = new List<MdlStructs.ShapeStruct>();
        var shapeMeshes = new List<MdlStructs.ShapeMeshStruct>();
        var shapeValues = new List<MdlStructs.ShapeValueStruct>();
        // TODO: elementIds
        var elementIds = new List<MdlStructs.ElementIdStruct>();
        var terrainShadowMeshStructs = new List<MdlStructs.TerrainShadowMeshStruct>();
        var terrainShadowSubmeshStructs = new List<MdlStructs.TerrainShadowSubmeshStruct>();
        var boneTableStructs = new List<MdlStructs.BoneTableStruct>();
        var submeshBoneMap = new List<ushort>();

        var min = new Vector4( 9999f, 9999f, 9999f, 1 );
        var max = new Vector4( -9999f, -9999f, -9999f, 1 );

        var shapeDict = new Dictionary<string, List<MdlStructs.ShapeStruct>>();
        var shapeMeshDict = new Dictionary<string, List<MdlStructs.ShapeMeshStruct>>();
        var shapeValuesDict = new Dictionary<string, List<MdlStructs.ShapeValueStruct>>();

        var vertexBufferOffset = 0;
        for( var i = 0; i < _meshBuilders.Count; i++ ) {
            var meshIndexData = new List<byte>();
            var meshBuilder = _meshBuilders[i];
            var vertexCount = meshBuilder.GetVertexCount( true, strings );
            var vertexBufferStride = GetVertexBufferStride( vertexDeclarations[i] ).ConvertAll( x => ( byte )x ).ToArray();
            boneTableStructs.Add( meshBuilder.GetBoneTableStruct( _stringTableBuilder.Bones.ToList(), _stringTableBuilder.HierarchyBones ) );

            var vertexBufferOffsets = new List<int>() { vertexBufferOffset, 0, 0 };
            for( var j = 1; j < 3; j++ ) {
                if( vertexBufferStride[j - 1] > 0 ) {
                    vertexBufferOffset += vertexBufferStride[j - 1] * vertexCount;
                }
                if( vertexBufferStride[j] > 0 ) {
                    vertexBufferOffsets[j] = vertexBufferOffset;
                }
            }

            meshStructs.Add( new() {
                VertexCount = ( ushort )vertexCount,
                IndexCount = ( ushort )meshBuilder.IndexCount,
                MaterialIndex = ( ushort )meshBuilder.GetMaterialIndex( _stringTableBuilder.Materials ),
                SubMeshIndex = ( ushort )submeshCounter,
                SubMeshCount = ( ushort )meshBuilder.Submeshes.Count,
                BoneTableIndex = ( ushort )i,
                StartIndex = ( uint )indexData.Count / 2,
                VertexBufferOffset = vertexBufferOffsets.ConvertAll( x => ( uint )x ).ToArray(),
                VertexBufferStride = vertexBufferStride,
                VertexStreamCount = ( byte )vertexBufferStride.Where( x => x > 0 ).Count()
            } );
            submeshCounter += meshBuilder.Submeshes.Count;

            var addedShapeVertices = 0;
            var accumulatedVertices = 0;

            //var bss = meshBuilder.GetSubmeshBoneMap( _stringTableBuilder.Bones.ToList() );
            //submeshBoneMap.AddRange( bss );

            for( var j = 0; j < meshBuilder.Bones.Count; j++ ) {
                submeshBoneMap.Add( ( ushort )j );
            }

            foreach( var submesh in meshBuilder.Submeshes ) {
                var bitangents = submesh.CalculateBitangents();
                var submeshIndexData = submesh.GetIndexData( accumulatedVertices );
                var submeshIndexOffset = ( indexData.Count + meshIndexData.Count ) / 2;

                meshIndexData.AddRange( submeshIndexData );

                submeshStructs.Add( new() {
                    IndexOffset = ( uint )submeshIndexOffset,
                    IndexCount = ( uint )submesh.IndexCount,
                    AttributeIndexMask = submesh.GetAttributeIndexMask( _stringTableBuilder.Attributes.ToList() ),
                    BoneStartIndex = ( ushort )boneCounter,
                    BoneCount = ( ushort )meshBuilder.Bones.Count
                } );

                boneCounter += meshBuilder.Bones.Count;
                addedShapeVertices += shapeValues.Count;
                accumulatedVertices += submesh.GetVertexCount( false, strings );

                // Assuming that this is how the bounding boxes are calculated
                min.X = min.X < submesh.MinBoundingBox.X ? min.X : submesh.MinBoundingBox.X;
                min.Y = min.Y < submesh.MinBoundingBox.Y ? min.Y : submesh.MinBoundingBox.Y;
                min.Z = min.Z < submesh.MinBoundingBox.Z ? min.Z : submesh.MinBoundingBox.Z;

                max.X = max.X > submesh.MaxBoundingBox.X ? max.X : submesh.MaxBoundingBox.X;
                max.Y = max.Y > submesh.MaxBoundingBox.Y ? max.Y : submesh.MaxBoundingBox.Y;
                max.Z = max.Z > submesh.MaxBoundingBox.Z ? max.Z : submesh.MaxBoundingBox.Z;

            }

            // Not sure if this is necessary
            /*
            while( meshIndexData.Count % 8 != 0 ) {
                meshIndexData.Add( ( byte )0 );
            }
            */
            indexData.AddRange( meshIndexData );
        }


        var shapeCount = 0;
        var shapeValueOffset = 0;
        var meshVertices = 0;
        var accumulatedIndices = 0;
        var meshVertexCount = new List<int>();
        for( var i = 0; i < _meshBuilders.Count; i++ ) {
            PluginLog.Debug( $"MeshVertices: {meshVertices}" );
            PluginLog.Debug( $"indices: {accumulatedIndices}" );
            var mesh = _meshBuilders[i];
            meshVertices = mesh.GetVertexCount( false, _stringTableBuilder.Shapes.ToList() );
            var meshVertexDict = mesh.GetVertexData();
            var meshShapeData = mesh.GetShapeData( _stringTableBuilder.Shapes.ToList() );

            PluginLog.Debug( $"meshshapeData.Keys: {meshShapeData.Count}" );
            var verticesFromShapes = 0;
            foreach( var shapeName in _stringTableBuilder.Shapes.ToList() ) {
                if (meshShapeData.ContainsKey(shapeName)) {
                    PluginLog.Debug( $"shapename: {shapeName}" );
                    var shapeVertexData = meshShapeData[shapeName];
                    var meshShapeValues = mesh.GetShapeValues( shapeName );

                    PluginLog.Debug( $"MeshShapeValues.Count: {meshShapeValues.Count}" );

                    foreach( var stream in shapeVertexData.Keys ) {
                        meshVertexDict[stream].AddRange( shapeVertexData[stream] );
                    }

                    shapeStructs.Add( new() {
                        StringOffset = _stringTableBuilder.GetShapeNameOffset( shapeName ),
                        ShapeMeshStartIndex = new ushort[] { ( ushort )shapeCount, 0, 0 },
                        ShapeMeshCount = new ushort[] { 1, 0, 0 }
                    } );
                    shapeMeshes.Add( new() {
                        MeshIndexOffset = ( uint )accumulatedIndices,
                        ShapeValueCount = ( uint )meshShapeValues.Count,
                        ShapeValueOffset = ( uint )shapeValueOffset
                    } );

                    var newShapeValues = new List<MdlStructs.ShapeValueStruct>();
                    foreach( var svs in meshShapeValues ) {
                        newShapeValues.Add( new() {
                            BaseIndicesIndex = ( ushort )( svs.BaseIndicesIndex /*+ accumulatedIndices*/ ),
                            ReplacingVertexIndex = ( ushort )( svs.ReplacingVertexIndex + meshVertices )
                        } );
                    }
                    //newShapeValues.Sort( CompareShapeValueStructs );
                    shapeValues.AddRange( newShapeValues );
                    verticesFromShapes += meshShapeValues.Count;
                    shapeCount++;
                }

            }
            //meshVertices += verticesFromShapes;
            accumulatedIndices += mesh.IndexCount;

            meshVertexCount.Add( meshVertices );
            foreach( var block in meshVertexDict.Values ) {
                vertexData.AddRange( block );
            }
        }

        PluginLog.Debug( $"ShapeStructs.Count: {shapeStructs.Count}" );
        foreach( var ss in shapeStructs ) {
            PluginLog.Debug( $"{ss.StringOffset}: {ss.ShapeMeshStartIndex[0]}-{ss.ShapeMeshStartIndex[1]}-{ss.ShapeMeshStartIndex[2]} || {ss.ShapeMeshCount[0]}-{ss.ShapeMeshCount[1]}-{ss.ShapeMeshCount[2]}" );
        }

        PluginLog.Debug( $"ShapeMeshes.Count: {shapeMeshes.Count}" );
        foreach( var sm in shapeMeshes ) {
            PluginLog.Debug( $"{sm.MeshIndexOffset}: {sm.ShapeValueCount} || {sm.ShapeValueOffset}" );
        }


        PluginLog.Debug( $"ShapeValues.Count: {shapeValues.Count}" );
        /*
        foreach( var sv in shapeValues ) {
            PluginLog.Debug( $"{sv.BaseIndicesIndex}:{sv.ReplacingVertexIndex}" );
        }
        */

        var filledBoundingBoxStruct = new MdlStructs.BoundingBoxStruct() {
            Min = new[] { min.X, min.Y, min.Z, min.W },
            Max = new[] { max.X, max.Y, max.Z, max.W }

        };
        var zeroBoundingBoxStruct = new MdlStructs.BoundingBoxStruct() {
            Min = new[] { 0f, 0, 0, 0 },
            Max = new[] { 0f, 0, 0, 0 }
        };

        var file = new MdlFile();

        file.FileHeader = new() {
            Version = _origModel.File?.FileHeader.Version ?? 16777220,
            StackSize = 0,   // To-be filled in later
            RuntimeSize = 0,    // To be filled later
            VertexDeclarationCount = ( ushort )vertexDeclarations.Length,
            MaterialCount = ( ushort )_stringTableBuilder.Materials.Count,
            VertexOffset = new uint[] { 0, 0, 0 },
            IndexOffset = new uint[] { 0, 0, 0 },
            VertexBufferSize = new uint[] { ( uint )vertexData.Count, 0, 0 },
            IndexBufferSize = new uint[] { ( uint )indexData.Count, 0, 0 },
            LodCount = 1,
            EnableIndexBufferStreaming = true,
            EnableEdgeGeometry = _origModel.File?.FileHeader.EnableEdgeGeometry ?? false
        };
        file.VertexDeclarations = vertexDeclarations.ToArray();
        file.StringCount = ( ushort )_stringTableBuilder.GetStringCount();
        file.Strings = _stringTableBuilder.GetBytes();
        file.ModelHeader = new() {
            Radius = _origModel.File.ModelHeader.Radius,
            MeshCount = ( ushort )meshStructs.Count,
            AttributeCount = ( ushort )_stringTableBuilder.Attributes.Count,
            SubmeshCount = ( ushort )submeshStructs.Count,
            MaterialCount = ( ushort )_stringTableBuilder.Materials.Count,
            BoneCount = ( ushort )_stringTableBuilder.Bones.Count,
            BoneTableCount = ( ushort )boneTableStructs.Count,

            ShapeCount = ( ushort )shapeStructs.Count,
            ShapeMeshCount = ( ushort )shapeMeshes.Count,
            ShapeValueCount = ( ushort )shapeValues.Count,
            LodCount = 1,

            ElementIdCount = ( ushort )elementIds.Count,

            TerrainShadowMeshCount = _origModel.File?.ModelHeader.TerrainShadowMeshCount ?? 0,

            ModelClipOutDistance = _origModel.File?.ModelHeader.ModelClipOutDistance ?? 0,
            ShadowClipOutDistance = _origModel.File?.ModelHeader.ShadowClipOutDistance ?? 0,
            Unknown4 = _origModel.File?.ModelHeader.Unknown4 ?? 0,
            TerrainShadowSubmeshCount = _origModel.File?.ModelHeader.TerrainShadowSubmeshCount ?? 0,
            BGChangeMaterialIndex = _origModel.File?.ModelHeader.BGChangeMaterialIndex ?? 0,
            BGCrestChangeMaterialIndex = _origModel.File?.ModelHeader.BGCrestChangeMaterialIndex ?? 0,
            Unknown6 = _origModel.File?.ModelHeader.Unknown6 ?? 0,
            Unknown7 = _origModel.File?.ModelHeader.Unknown7 ?? 0,
            Unknown8 = _origModel.File?.ModelHeader.Unknown8 ?? 0,
            Unknown9 = _origModel.File?.ModelHeader.Unknown9 ?? 0,
        };
        file.ElementIds = elementIds.ToArray();

        file.Meshes = meshStructs.ToArray();
        file.AttributeNameOffsets = _stringTableBuilder.GetAttributeNameOffsets();
        file.TerrainShadowMeshes = terrainShadowMeshStructs.ToArray();
        file.Submeshes = submeshStructs.ToArray();
        file.TerrainShadowSubmeshes = terrainShadowSubmeshStructs.ToArray();

        file.MaterialNameOffsets = _stringTableBuilder.GetMaterialNameOffsets();
        file.BoneNameOffsets = _stringTableBuilder.GetBoneNameOffsets();
        file.BoneTables = boneTableStructs.ToArray();

        file.Shapes = shapeStructs.ToArray();
        file.ShapeMeshes = shapeMeshes.ToArray();
        file.ShapeValues = shapeValues.ToArray();
        file.SubmeshBoneMap = submeshBoneMap.ToArray();

        var stackSize = vertexDeclarations.Length * 136;
        var runtimeSize =
            2   //StringCount
            + 2 // Unknown
            + 4 //StringSize
            + file.Strings.Length
            + 56    //ModelHeader
            + ( file.ElementIds.Length * 32 )
            + ( 3 * 60 )  // 3 Lods
            + ( file.ModelHeader.ExtraLodEnabled ? 40 : 0 )
            + file.Meshes.Length * 36
            + file.AttributeNameOffsets.Length * 4
            + file.TerrainShadowMeshes.Length * 20
            + file.Submeshes.Length * 16
            + file.TerrainShadowSubmeshes.Length * 10
            + file.MaterialNameOffsets.Length * 4
            + file.BoneNameOffsets.Length * 4
            + file.BoneTables.Length * 132
            + file.Shapes.Length * 16
            + file.ShapeMeshes.Length * 12
            + file.ShapeValues.Length * 4
            + 4 // SubmeshBoneMapSize
            + file.SubmeshBoneMap.Length * 2
            + 8 // PaddingAmount and Padding
            + ( 4 * 32 )  // 4 BoundingBoxes
            + ( file.ModelHeader.BoneCount * 32 );
        var vertexOffset0 = runtimeSize
            + 68    // ModelFileHeader
            + stackSize;
        var indexOffset0 = ( uint )( vertexOffset0 + file.FileHeader.VertexBufferSize[0] );
        var fileSize = indexOffset0 + file.FileHeader.IndexBufferSize[0];

        var meshIndex = 0;
        var lods = new List<MdlStructs.LodStruct>();
        for( var i = 0; i < 3; i++ ) {
            lods.Add( new() {
                MeshIndex = ( ushort )meshIndex,
                MeshCount = ( i == 0 ) ? ( ushort )meshStructs.Count : ( ushort )0,
                ModelLodRange = 1,  // idk
                TextureLodRange = 1,    //idk
                WaterMeshIndex = ( ushort )meshStructs.Count, // idk
                WaterMeshCount = 0,
                ShadowMeshIndex = ( ushort )meshStructs.Count,    // idk
                ShadowMeshCount = 0,
                TerrainShadowMeshIndex = 0, // maybe idk?
                TerrainShadowMeshCount = 0,
                VerticalFogMeshIndex = ( ushort )meshIndex, // probably idk
                VerticalFogMeshCount = 0,
                EdgeGeometrySize = 0,
                EdgeGeometryDataOffset = ( i == 0 ) ? indexOffset0 : fileSize,
                PolygonCount = 0,
                Unknown1 = 0,
                VertexBufferSize = ( i == 0 ) ? ( uint )vertexData.Count : 0,
                IndexBufferSize = ( i == 0 ) ? ( uint )indexData.Count : 0,
                VertexDataOffset = ( i == 0 ) ? ( uint )vertexOffset0 : fileSize,
                IndexDataOffset = ( i == 0 ) ? ( uint )indexOffset0 : fileSize
            } );

            if( i == 0 ) {
                meshIndex += meshStructs.Count;
            }
        }

        file.Lods = lods.ToArray();
        if( file.ModelHeader.ExtraLodEnabled ) {
            // TODO: ExtraLods
        }

        // TODO: Bounding boxes? Specifically WaterBoundingBoxes and VerticalFogBoundingBoxes?
        file.BoundingBoxes = filledBoundingBoxStruct;
        file.ModelBoundingBoxes = filledBoundingBoxStruct;
        file.WaterBoundingBoxes = zeroBoundingBoxStruct;
        file.VerticalFogBoundingBoxes = zeroBoundingBoxStruct;

        file.BoneBoundingBoxes = new MdlStructs.BoundingBoxStruct[_stringTableBuilder.Bones.Count];
        for( var i = 0; i < _stringTableBuilder.Bones.Count; i++ ) {
            file.BoneBoundingBoxes[i] = zeroBoundingBoxStruct;
        }

        file.FileHeader.StackSize = ( uint )stackSize;
        file.FileHeader.RuntimeSize = ( uint )runtimeSize;
        file.FileHeader.VertexOffset[0] = ( uint )vertexOffset0;
        file.FileHeader.IndexOffset[0] = ( uint )indexOffset0;

        PluginLog.Debug( "Ending" );
        return (file, vertexData, indexData);
    }

    private static List<int> GetVertexBufferStride( MdlStructs.VertexDeclarationStruct vds ) {
        var vertexDeclarations = new List<MdlStructs.VertexElement>[3] {
            new(), new(), new()
        };
        foreach( var ele in vds.VertexElements ) {
            if( ele.Stream == 255 ) {
                break;
            }
            else {
                vertexDeclarations[ele.Stream].Add( ele );
            }
        }

        var vertexDeclarationSizes = new List<int>() { 0, 0, 0 };
        for( var i = 0; i < 3; i++ ) {
            if( vertexDeclarations[i].Count == 0 ) {
                continue;
            }

            var lastEle = vertexDeclarations[i].Last();

            vertexDeclarationSizes[i] = ( Vertex.VertexType )lastEle.Type switch {
                Vertex.VertexType.Single3 => 12,
                Vertex.VertexType.Single4 => 16,
                Vertex.VertexType.UInt => 4,
                Vertex.VertexType.ByteFloat4 => 4,
                Vertex.VertexType.Half2 => 4,
                Vertex.VertexType.Half4 => 8,
                _ => throw new ArgumentOutOfRangeException( $"Unknown VertexType: {( Vertex.VertexType )lastEle.Type}" )
            } + lastEle.Offset;
        }

        return vertexDeclarationSizes;
    }
}