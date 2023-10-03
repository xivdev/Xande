using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System.Collections;
using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xande.Models.Export;
using Mesh = SharpGLTF.Schema2.Mesh;

namespace Xande.Models.Import;

// https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs
// https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Files/MdlFile.cs
public class MdlFileBuilder {
    private ModelRoot _root;
    private Model? _origModel;
    private ILogger? _logger;

    private SortedDictionary< int, SortedDictionary< int, Mesh > > _meshes = new();
    private StringTableBuilder                                     _stringTableBuilder;
    private List< LuminaMeshBuilder >                              _meshBuilders = new();

    private SortedDictionary< int, SortedDictionary< int, List< string > > > _addedAttributes = new();


    public MdlFileBuilder( ModelRoot root, Model? model, ILogger? logger = null ) {
        _root = root;
        _origModel = model;
        _logger = logger;
        _stringTableBuilder = new StringTableBuilder( _logger );

        foreach( var node in root.LogicalNodes ) {
            if( node.Mesh != null ) {
                var mesh = node.Mesh;
                var name = node.Name;
                if( name is null ) continue;
                var match = Regex.Match( name, @"([0-9]+\.[0-9]+)" );
                if( match.Success ) {
                    var str = match.Groups[ 1 ].Value;

                    var isSubmesh = str.Contains( '.' );
                    if( isSubmesh ) {
                        var parts      = str.Split( '.' );
                        var meshIdx    = int.Parse( parts[ 0 ] );
                        var submeshIdx = int.Parse( parts[ 1 ] );
                        if( !_meshes.ContainsKey( meshIdx ) ) { _meshes[ meshIdx ] = new(); }

                        _meshes[ meshIdx ][ submeshIdx ] = mesh;
                    } else {
                        var meshIdx = int.Parse( str );

                        if( !_meshes.ContainsKey( meshIdx ) ) _meshes[ meshIdx ] = new();
                        _meshes[ meshIdx ][ -1 ] = mesh;
                    }
                }
                else {
                    _logger?.Debug( $"Skipping \"{name}\"" );
                }
            }
        }
    }

    public bool AddAttribute( string name, int mesh, int submesh ) {
        if( _meshes.ContainsKey( mesh ) && _meshes[ mesh ].ContainsKey( submesh ) ) {
            if( !_addedAttributes.ContainsKey( mesh ) ) { _addedAttributes[ mesh ]                       = new(); }
            if( !_addedAttributes[ mesh ].ContainsKey( submesh ) ) { _addedAttributes[ mesh ][ submesh ] = new(); }
            if( !_addedAttributes[ mesh ][ submesh ].Contains( name ) ) {
                _addedAttributes[ mesh ][ submesh ].Add( name );
                return true;
            }
        }
        return false;
    }

    private List< string > GetJoints( List< Node > children, List< string > matches ) {
        List< string > ret = new();
        if( children != null && children.Count() > 0 ) {
            for( int i = 0; i < children.Count(); i++ ) {
                if( matches.Contains( children[ i ].Name ) ) { ret.Add( children[ i ].Name ); }
                ret.AddRange( GetJoints( children[ i ].VisualChildren.ToList(), matches ) );
            }
        }

        return ret;
    }

    private MdlStructs.VertexDeclarationStruct[] GetVertexDeclarationStructs( int size, MdlStructs.VertexDeclarationStruct? vds = null ) {
        var ret = new List<MdlStructs.VertexDeclarationStruct>();
        // Hard-coded or pull whatever the original model had?
        /*
        if( vds != null ) {
            for( var i = 0; i < size; i++ ) {
                var ve = new List<MdlStructs.VertexElement>();
                for( var j = 0; j < vds.Value.VertexElements.Length; j++ ) {
                    ve.Add( vds.Value.VertexElements[j] );
                }
                if( ve.Last().Stream != 255 ) {
                    ve.Add( new() {
                        Stream = 255
                    } );
                }
                while( ve.Count < 17 ) {
                    ve.Add( new() {
                        Stream = 0,
                        Offset = 0,
                        Usage = 0,
                        Type = 0,
                        UsageIndex = 0
                    } );
                }
                var dec = new MdlStructs.VertexDeclarationStruct() {
                    VertexElements = ve.ToArray()
                };
                ret.Add( dec );
            }
        }
        else {
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
        for( var i = 0; i < size; i++ ) {
            ret.Add( declaration );
        }
        //}
        return ret.ToArray();
    }

    public (MdlFile? file, List< byte > vertexData, List< byte > indexData) Build() {
        var start              = DateTime.Now;
        var vertexDeclarations = GetVertexDeclarationStructs( _meshes.Keys.Count, _origModel?.File?.VertexDeclarations[ 0 ] );

        var allBones = new List<string>();
        var bonesToNodes = new Dictionary<string, Node>();
        var eidNodes = new SortedDictionary<string, Node>();

        Skin? skeleton = null;
        if( _root.LogicalSkins.Count == 0 ) {
            if( _origModel?.File?.ModelHeader.BoneCount > 0 ) {
                _logger?.Error( $"The input model had no skeleton/armature while the original model does. This will likely crash the game." );
                return (null, new List<byte>(), new List<byte>());
            }
        } else {
            skeleton = _root.LogicalSkins[ 0 ];
            if( skeleton != null ) {
                for( var id = 0; id < skeleton.JointsCount; id++ ) {
                    var (joint, InverseBindMatrix) = skeleton.GetJoint( id );

                    var boneString = joint.Name;
                    if( !String.IsNullOrEmpty( boneString ) ) {
                        allBones.Add( boneString );
                        bonesToNodes.Add( boneString, joint );

                        if( boneString.StartsWith( "EID_" ) ) {
                            eidNodes.Add( boneString, joint );
                        }
                    }
                }
            }
            else {
                _logger?.Error( $"Skeleton was somehow null" );
                _logger?.Error( $"The input model had no skeleton/armature while the original model does. This will likely crash the game." );
                return (null, new List<byte>(), new List<byte>());
            }
        }

        foreach( var meshIdx in _meshes.Keys ) {
            var submeshes = new List<SubmeshBuilder>();
            foreach( var submeshIdx in _meshes[meshIdx].Keys ) {
                var vd = vertexDeclarations.Length > meshIdx ? vertexDeclarations[meshIdx] : vertexDeclarations[0];

                var submesh = new SubmeshBuilder( _meshes[meshIdx][submeshIdx], allBones, vd, _logger );
                if( _addedAttributes.ContainsKey( meshIdx ) && _addedAttributes[meshIdx].ContainsKey( submeshIdx ) ) {
                    foreach( var attr in _addedAttributes[meshIdx][submeshIdx] ) {
                        if( submesh.AddAttribute( attr ) ) {

                        }
                        else {
                            _logger?.Warning( $"Could not add attribute: \"{attr}\" at mesh {meshIdx}, submesh {submeshIdx}" );
                        }
                    }
                }

                submeshes.Add( submesh );
            }
            var meshBuilder = new LuminaMeshBuilder( submeshes, _logger );
            _meshBuilders.Add( meshBuilder );
            _stringTableBuilder.AddBones( meshBuilder.Bones );
            _stringTableBuilder.AddMaterial( meshBuilder.Material );
            _stringTableBuilder.AddShapes( meshBuilder.Shapes );
            _stringTableBuilder.AddAttributes( meshBuilder.Attributes );
        }
        var inputMaterials = _stringTableBuilder.Materials.Where( m => m.StartsWith( '/' ) );

        if( _origModel != null ) {
            if( inputMaterials.Any() ^ _origModel.Materials.Where( m => m.MaterialPath.StartsWith( '/' ) ).Any() ) {
                if( inputMaterials.Any() ) {
                    _logger?.Warning( $"Input model has a material starting with / while the original model does not." );
                }
                else {
                    _logger?.Warning( $"Input model materials do not start with / while the original model does." );
                }
                _logger?.Warning( $"This will likely crash the game if left unchanged." );
            }
        }
        else if( inputMaterials.Any() ^ _stringTableBuilder.Bones.Count > 0 ) {
            // Not strictly true
            // But equipment seems to expect a material starting with / and equipment needs a skeleton
            _logger?.Warning( $"Input model has materials starting with / while a skeleton is active." );
            _logger?.Warning( $"This may crash the game." );
        }

        if( _stringTableBuilder.Bones.Where( x => x.Contains( "n_hara" ) ).Count() > 1 ) {
            // TODO: ?
        }

        if( skeleton != null ) {
            _stringTableBuilder.HierarchyBones = GetJoints( skeleton.GetJoint( 0 ).Joint.VisualChildren.ToList(), _stringTableBuilder.Bones.ToList() );
        }

        var strings = _stringTableBuilder.GetStrings();

        var vertexData = new List< byte >();
        var vertexDict = new Dictionary< int, List< byte > >();
        var indexData  = new List< byte >();

        var submeshCounter = 0;
        var boneCounter    = 0;
        var meshStructs = new List<MdlStructs.MeshStruct>();
        var submeshStructs = new List<MdlStructs.SubmeshStruct>();
        var shapeStructs = new List<MdlStructs.ShapeStruct>();
        var shapeMeshes = new List<MdlStructs.ShapeMeshStruct>();
        var shapeValues = new List<MdlStructs.ShapeValueStruct>();
        // TODO: elementIds?
        var elementIds = new List<MdlStructs.ElementIdStruct>();
        var terrainShadowMeshStructs = new List<MdlStructs.TerrainShadowMeshStruct>();
        var terrainShadowSubmeshStructs = new List<MdlStructs.TerrainShadowSubmeshStruct>();
        var boneTableStructs = new List<MdlStructs.BoneTableStruct>();
        var submeshBoneMap = new List<ushort>();

        var eidCounter = 3;

        /*
        // unsure if we can produce ElementIds solely from an external model
        // I don't think reversing is the actual answer, as, based on the names they have a certain hierarchy
        // ex: EID_W_EDGE_END,  EID_W_EDGE_MD,  EID_W_EDGE_ST denoting end, middle, start respectively
        var reverseEidNodes = eidNodes.Reverse();
        foreach( var (boneName, joint) in reverseEidNodes ) {
            var transform = joint.LocalTransform;
            var translate = transform.Translation;
            var rotation = transform.Rotation;
            var parent = joint.VisualParent;
            var parentName = parent.Name;

            if( !_stringTableBuilder.Bones.Contains( parentName ) ) {
                _stringTableBuilder.AddBone( parentName );
            }

            var parentNameOffset = _stringTableBuilder.GetOffset( parentName );

            var eid = new MdlStructs.ElementIdStruct() {
                ElementId      = ( uint )eidCounter,
                ParentBoneName = parentNameOffset,
                // probably not actually accurate
                Translate = new float[] { -translate.Y, translate.X, translate.Z },
                Rotate = new float[] { rotation.X, rotation.Y, rotation.Z }
            };
            eidCounter++;
            elementIds.Add( eid );
        }
        */

        if( eidNodes.Count == 0 && _origModel?.File?.ElementIds != null ) {
            foreach( var eid in _origModel.File.ElementIds ) {
                elementIds.Add( eid );
            }
        }

        var min = new Vector4( 9999f, 9999f, 9999f, 1 );
        var max = new Vector4( -9999f, -9999f, -9999f, 1 );

        var vertexBufferOffset = 0;
        uint totalIndexCount = 0;
        var meshIndexOffsetDict = new SortedDictionary<string, List<(uint, uint, List<MdlStructs.ShapeValueStruct>)>>();
        uint meshIndexCount = 0;

        for( var i = 0; i < _meshBuilders.Count; i++ ) {
            var mesh               = _meshBuilders[ i ];
            var meshIndexData      = new List< byte >();
            var vertexCount        = mesh.GetVertexCount( true, strings );
            var vertexBufferStride = GetVertexBufferStride( vertexDeclarations[ i ] ).ConvertAll( x => ( byte )x ).ToArray();
            boneTableStructs.Add( mesh.GetBoneTableStruct( _stringTableBuilder.Bones.ToList(), _stringTableBuilder.HierarchyBones ) );

            var vertexBufferOffsets = new List< int >() { vertexBufferOffset, 0, 0 };
            for( var j = 1; j < 3; j++ ) {
                if( vertexBufferStride[ j - 1 ] > 0 ) { vertexBufferOffset   += vertexBufferStride[ j - 1 ] * vertexCount; }
                if( vertexBufferStride[ j ] > 0 ) { vertexBufferOffsets[ j ] =  vertexBufferOffset; }
            }
            if (vertexCount > ushort.MaxValue) {
                _logger?.Error( $"There are too many vertices ({vertexCount}) in mesh {i}. Limit of {ushort.MaxValue}" );
            }

            meshStructs.Add( new() {
                VertexCount        = ( ushort )vertexCount,
                IndexCount         = ( uint )mesh.IndexCount,
                MaterialIndex      = ( ushort )mesh.GetMaterialIndex( _stringTableBuilder.Materials.ToList() ),
                SubMeshIndex       = ( ushort )submeshCounter,
                SubMeshCount       = ( ushort )mesh.Submeshes.Count,
                BoneTableIndex     = ( ushort )i,
                StartIndex         = ( uint )indexData.Count / 2,
                VertexBufferOffset = vertexBufferOffsets.ConvertAll( x => ( uint )x ).ToArray(),
                VertexBufferStride = vertexBufferStride,
                VertexStreamCount  = ( byte )vertexBufferStride.Where( x => x > 0 ).Count()
            } );
            submeshCounter += mesh.Submeshes.Count;

            for( var j = 0; j < mesh.Bones.Count; j++ ) { submeshBoneMap.Add( ( ushort )j ); }

            var mvd = new MeshVertexData( _logger );
            //var meshVertexDict = mesh.GetVertexData();
            mvd.AddVertexData( mesh.GetVertexData() );
            totalIndexCount = 0;

            var shapeVertices = 0;
            uint submeshIndexCount = 0;
            var meshVertexCount = 0;

            for( var j = 0; j < mesh.Submeshes.Count; j++ ) {
                var submesh          = mesh.Submeshes[ j ];
                var submeshIndexData = submesh.GetIndexData( meshVertexCount );
                totalIndexCount += submesh.IndexCount;
                var submeshIndexOffset = ( indexData.Count + meshIndexData.Count ) / 2;

                meshIndexData.AddRange( submeshIndexData );

                submeshStructs.Add( new() {
                    IndexOffset        = ( uint )submeshIndexOffset,
                    IndexCount         = ( uint )submesh.IndexCount,
                    AttributeIndexMask = submesh.GetAttributeIndexMask( _stringTableBuilder.Attributes.ToList() ),
                    BoneStartIndex     = ( ushort )boneCounter,
                    BoneCount          = ( ushort )mesh.Bones.Count
                } );

                boneCounter += mesh.Bones.Count;

                // Assuming that this is how the bounding boxes are calculated
                min.X = min.X < submesh.MinBoundingBox.X ? min.X : submesh.MinBoundingBox.X;
                min.Y = min.Y < submesh.MinBoundingBox.Y ? min.Y : submesh.MinBoundingBox.Y;
                min.Z = min.Z < submesh.MinBoundingBox.Z ? min.Z : submesh.MinBoundingBox.Z;

                max.X = max.X > submesh.MaxBoundingBox.X ? max.X : submesh.MaxBoundingBox.X;
                max.Y = max.Y > submesh.MaxBoundingBox.Y ? max.Y : submesh.MaxBoundingBox.Y;
                max.Z = max.Z > submesh.MaxBoundingBox.Z ? max.Z : submesh.MaxBoundingBox.Z;

                var submeshShapeData = submesh.GetShapeVertexData( _stringTableBuilder.Shapes.ToList() );
                foreach( var (shapeName, values) in submeshShapeData ) {
                    var newShapeValues = new List< MdlStructs.ShapeValueStruct >();
                    mvd.AddShapeVertexData( values );
                    var submeshShapeValues = submesh.GetShapeValues( shapeName );

                    foreach( var svs in submeshShapeValues ) {
                        if( svs.BaseIndicesIndex + submeshIndexCount > ushort.MaxValue ) {
                            _logger?.Error( $"Shape {shapeName} in submesh {i}-{j} has too many indices." );
                        }
                        if( svs.ReplacingVertexIndex + mesh.GetVertexCount() + shapeVertices > ushort.MaxValue ) {
                            _logger?.Error( $"Shape {shapeName} in submesh {i}-{j} has too many vertices." );
                        }
                        var newShapeValue = new MdlStructs.ShapeValueStruct() {
                            BaseIndicesIndex     = ( ushort )( svs.BaseIndicesIndex + submeshIndexCount ),
                            ReplacingVertexIndex = ( ushort )( svs.ReplacingVertexIndex + mesh.GetVertexCount() + shapeVertices )
                        };
                        newShapeValues.Add( newShapeValue );
                    }
                    if( !meshIndexOffsetDict.ContainsKey( shapeName ) ) { meshIndexOffsetDict.Add( shapeName, new() ); }
                    meshIndexOffsetDict[ shapeName ].Add( ( ( uint )meshIndexCount, ( uint )submeshShapeValues.Count, newShapeValues ) );
                    /*
                     * Seems like we CAN have each ShapeStruct added individually
                     * However, it also seems that the original models have them grouped up by shape name
                    shapeStructs.Add( new() {
                        StringOffset = _stringTableBuilder.GetShapeNameOffset( shapeName ),
                        ShapeMeshStartIndex = new ushort[] { ( ushort )shapeMeshStartIndex, 0, 0 },
                        ShapeMeshCount = new ushort[] { ( ushort )1, 0, 0 }
                    } );
                    shapeMeshes.Add( new() {
                        MeshIndexOffset = ( uint )meshIndexCount,
                        ShapeValueCount = ( uint )submeshShapeValues.Count,
                        ShapeValueOffset = ( uint )shapeValueOffset
                    } );
                    shapeMeshStartIndex ++;
                    shapeValueOffset += submeshShapeValues.Count;
                    shapeValues.AddRange( newShapeValues );
                    */

                    shapeVertices += submesh.GetShapeVertexCount( shapeName );
                }

                meshVertexCount   += submesh.GetVertexCount();
                submeshIndexCount += submesh.IndexCount;
            }

            meshIndexCount += mesh.IndexCount;
            vertexData.AddRange( mvd.GetBytes() );
            indexData.AddRange( meshIndexData );
        }

        var  shapeMeshStartIndex = 0;
        uint shapeValueOffset    = 0;
        foreach( var kvp in meshIndexOffsetDict ) {
            var shapeMeshCount = meshIndexOffsetDict[ kvp.Key ].Count;
            shapeStructs.Add( new() {
                StringOffset        = _stringTableBuilder.GetShapeNameOffset( kvp.Key ),
                ShapeMeshStartIndex = new ushort[] { ( ushort )shapeMeshStartIndex, 0, 0 },
                ShapeMeshCount      = new ushort[] { ( ushort )shapeMeshCount, 0, 0 }
            } );
            shapeMeshStartIndex += shapeMeshCount;

            foreach( var (offset, svCount, svs) in meshIndexOffsetDict[ kvp.Key ] ) {
                shapeMeshes.Add( new() {
                    MeshIndexOffset  = offset,
                    ShapeValueCount  = svCount,
                    ShapeValueOffset = ( uint )shapeValueOffset
                } );
                shapeValueOffset += svCount;
                shapeValues.AddRange( svs ); // the shape values have to be placed in the same order as the corresponding ShapeMeshStruct
            }
        }

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
            Version                    = _origModel?.File?.FileHeader.Version ?? 16777220,
            StackSize                  = 0, // To-be filled in later
            RuntimeSize                = 0, // To be filled later
            VertexDeclarationCount     = ( ushort )vertexDeclarations.Length,
            MaterialCount              = ( ushort )_stringTableBuilder.Materials.Count,
            VertexOffset               = new uint[] { 0, 0, 0 },
            IndexOffset                = new uint[] { 0, 0, 0 },
            VertexBufferSize           = new uint[] { ( uint )vertexData.Count, 0, 0 },
            IndexBufferSize            = new uint[] { ( uint )indexData.Count, 0, 0 },
            LodCount                   = 1,
            EnableIndexBufferStreaming = true,
            EnableEdgeGeometry         = _origModel?.File?.FileHeader.EnableEdgeGeometry ?? false
        };
        file.VertexDeclarations = vertexDeclarations.ToArray();
        file.StringCount        = ( ushort )_stringTableBuilder.GetStringCount();
        file.Strings            = _stringTableBuilder.GetBytes();
        file.ModelHeader = new() {
            Radius         = _origModel?.File.ModelHeader.Radius ?? 0,
            MeshCount      = ( ushort )meshStructs.Count,
            AttributeCount = ( ushort )_stringTableBuilder.Attributes.Count,
            SubmeshCount   = ( ushort )submeshStructs.Count,
            MaterialCount  = ( ushort )_stringTableBuilder.Materials.Count,
            BoneCount      = ( ushort )_stringTableBuilder.Bones.Count,
            BoneTableCount = ( ushort )boneTableStructs.Count,

            ShapeCount      = ( ushort )shapeStructs.Count,
            ShapeMeshCount  = ( ushort )shapeMeshes.Count,
            ShapeValueCount = ( ushort )shapeValues.Count,
            LodCount        = 1,

            ElementIdCount = ( ushort )elementIds.Count,

            TerrainShadowMeshCount = _origModel?.File?.ModelHeader.TerrainShadowMeshCount ?? 0,

            ModelClipOutDistance       = _origModel?.File?.ModelHeader.ModelClipOutDistance ?? 0,
            ShadowClipOutDistance      = _origModel?.File?.ModelHeader.ShadowClipOutDistance ?? 0,
            Unknown4                   = _origModel?.File?.ModelHeader.Unknown4 ?? 0,
            TerrainShadowSubmeshCount  = _origModel?.File?.ModelHeader.TerrainShadowSubmeshCount ?? 0,
            BGChangeMaterialIndex      = _origModel?.File?.ModelHeader.BGChangeMaterialIndex ?? 0,
            BGCrestChangeMaterialIndex = _origModel?.File?.ModelHeader.BGCrestChangeMaterialIndex ?? 0,
            Unknown6                   = _origModel?.File?.ModelHeader.Unknown6 ?? 0,
            Unknown7                   = _origModel?.File?.ModelHeader.Unknown7 ?? 0,
            Unknown8                   = _origModel?.File?.ModelHeader.Unknown8 ?? 0,
            Unknown9                   = _origModel?.File?.ModelHeader.Unknown9 ?? 0,
        };
        file.ElementIds = elementIds.ToArray();

        file.Meshes                 = meshStructs.ToArray();
        file.AttributeNameOffsets   = _stringTableBuilder.GetAttributeNameOffsets();
        file.TerrainShadowMeshes    = terrainShadowMeshStructs.ToArray();
        file.Submeshes              = submeshStructs.ToArray();
        file.TerrainShadowSubmeshes = terrainShadowSubmeshStructs.ToArray();

        file.MaterialNameOffsets = _stringTableBuilder.GetMaterialNameOffsets();
        file.BoneNameOffsets     = _stringTableBuilder.GetBoneNameOffsets();
        file.BoneTables          = boneTableStructs.ToArray();

        file.Shapes         = shapeStructs.ToArray();
        file.ShapeMeshes    = shapeMeshes.ToArray();
        file.ShapeValues    = shapeValues.ToArray();
        file.SubmeshBoneMap = submeshBoneMap.ToArray();

        var stackSize = vertexDeclarations.Length * 136;
        var runtimeSize =
            2   //StringCount
            + 2 // Unknown
            + 4 //StringSize
            + file.Strings.Length
            + 56 //ModelHeader
            + ( file.ElementIds.Length * 32 )
            + ( 3 * 60 ) // 3 Lods
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
            + 8          // PaddingAmount and Padding
            + ( 4 * 32 ) // 4 BoundingBoxes
            + ( file.ModelHeader.BoneCount * 32 );

        var vertexOffset0 = runtimeSize
                            + 68 // ModelFileHeader
                            + stackSize;
        var indexOffset0 = ( uint )( vertexOffset0 + file.FileHeader.VertexBufferSize[ 0 ] );
        var fileSize     = indexOffset0 + file.FileHeader.IndexBufferSize[ 0 ];

        var meshIndex = 0;
        var lods      = new List< MdlStructs.LodStruct >();
        for( var i = 0; i < 3; i++ ) {
            lods.Add( new() {
                MeshIndex              = ( ushort )meshIndex,
                MeshCount              = ( i == 0 ) ? ( ushort )meshStructs.Count : ( ushort )0,
                ModelLodRange          = 1,                           // idk
                TextureLodRange        = 1,                           //idk
                WaterMeshIndex         = ( ushort )meshStructs.Count, // idk
                WaterMeshCount         = 0,
                ShadowMeshIndex        = ( ushort )meshStructs.Count, // idk
                ShadowMeshCount        = 0,
                TerrainShadowMeshIndex = 0, // maybe idk?
                TerrainShadowMeshCount = 0,
                VerticalFogMeshIndex   = ( ushort )meshIndex, // probably idk
                VerticalFogMeshCount   = 0,
                EdgeGeometrySize       = 0,
                EdgeGeometryDataOffset = ( i == 0 ) ? indexOffset0 : fileSize,
                PolygonCount           = 0,
                Unknown1               = 0,
                VertexBufferSize       = ( i == 0 ) ? ( uint )vertexData.Count : 0,
                IndexBufferSize        = ( i == 0 ) ? ( uint )indexData.Count : 0,
                VertexDataOffset       = ( i == 0 ) ? ( uint )vertexOffset0 : fileSize,
                IndexDataOffset        = ( i == 0 ) ? ( uint )indexOffset0 : fileSize
            } );

            if( i == 0 ) { meshIndex += meshStructs.Count; }
        }

        file.Lods = lods.ToArray();
        if( file.ModelHeader.ExtraLodEnabled ) {
            // TODO: ExtraLods?
        }

        // TODO: Bounding boxes? Specifically WaterBoundingBoxes and VerticalFogBoundingBoxes?
        file.BoundingBoxes            = filledBoundingBoxStruct;
        file.ModelBoundingBoxes       = filledBoundingBoxStruct;
        file.WaterBoundingBoxes       = zeroBoundingBoxStruct;
        file.VerticalFogBoundingBoxes = zeroBoundingBoxStruct;

        file.BoneBoundingBoxes = new MdlStructs.BoundingBoxStruct[_stringTableBuilder.Bones.Count];
        for( var i = 0; i < _stringTableBuilder.Bones.Count; i++ ) { file.BoneBoundingBoxes[ i ] = zeroBoundingBoxStruct; }

        file.FileHeader.StackSize         = ( uint )stackSize;
        file.FileHeader.RuntimeSize       = ( uint )runtimeSize;
        file.FileHeader.VertexOffset[ 0 ] = ( uint )vertexOffset0;
        file.FileHeader.IndexOffset[ 0 ]  = ( uint )indexOffset0;

        _logger?.Debug( $"Ending. REGULAR took: {DateTime.Now - start}" );
        return (file, vertexData, indexData);
    }

    private static List< int > GetVertexBufferStride( MdlStructs.VertexDeclarationStruct vds ) {
        var vertexDeclarations = new List< MdlStructs.VertexElement >[3] {
            new(), new(), new()
        };
        foreach( var ele in vds.VertexElements ) {
            if( ele.Stream == 255 ) { break; } else { vertexDeclarations[ ele.Stream ].Add( ele ); }
        }

        var vertexDeclarationSizes = new List< int >() { 0, 0, 0 };
        for( var i = 0; i < 3; i++ ) {
            if( vertexDeclarations[ i ].Count == 0 ) { continue; }

            var lastEle = vertexDeclarations[ i ].Last();

            vertexDeclarationSizes[ i ] = ( Vertex.VertexType )lastEle.Type switch {
                Vertex.VertexType.Single3    => 12,
                Vertex.VertexType.Single4    => 16,
                Vertex.VertexType.UInt       => 4,
                Vertex.VertexType.ByteFloat4 => 4,
                Vertex.VertexType.Half2      => 4,
                Vertex.VertexType.Half4      => 8,
                _                            => throw new ArgumentOutOfRangeException( $"Unknown VertexType: {( Vertex.VertexType )lastEle.Type}" )
            } + lastEle.Offset;
        }

        return vertexDeclarationSizes;
    }
}