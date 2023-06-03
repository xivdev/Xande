using Dalamud.Logging;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mesh = SharpGLTF.Schema2.Mesh;

namespace Xande.Models.Import;

// https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs
// https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Files/MdlFile.cs
public class MdlFileWriter : IDisposable {
    private ModelRoot _root;
    private Model _origModel;

    private BinaryWriter _w;

    private Dictionary<int, Dictionary<int, Mesh>> _meshes = new();
    private StringTableBuilder _stringTableBuilder;

    private uint _vertexOffset = 0;
    private uint _indexOffset = 0;
    private uint _vertexBufferSize = 0;
    private uint _indexBufferSize = 0;

    public MdlFileWriter( ModelRoot root, Model model, Stream stream ) {
        _root = root;
        _origModel = model;

        _w = new BinaryWriter( stream );

        _stringTableBuilder = new StringTableBuilder( _root );
        foreach( var b in _stringTableBuilder.Bones ) {
            PluginLog.Debug( b );
        }

        foreach( var mesh in root.LogicalMeshes ) {
            var name = mesh.Name;
            PluginLog.Debug( name );
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

                    PluginLog.Debug( "Adding {x},{y}", meshIdx, submeshIdx );
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

    private void WriteFileHeader() {
        var origHeader = _origModel.File!.FileHeader;

        _w.Write( origHeader.Version );
        _w.Write( ( uint )0 );   // Stack size TODO
        _w.Write( ( uint )0 );   // Runtime size TODO
        _w.Write( ( ushort )0 ); // Vertex declaration len TODO
        _w.Write( ( ushort )0 ); // Material len TODO

        // Vertex offsets TODO
        //for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }
        _w.Write( _vertexOffset );
        _w.Write( ( uint )0 );
        _w.Write( ( uint )0 );

        // Index offsets TODO
        //for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }
        _w.Write( _indexOffset );
        _w.Write( ( uint )0 );
        _w.Write( ( uint )0 );

        // Vertex buffer size TODO
        //for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }
        _w.Write( _vertexBufferSize );
        _w.Write( ( uint )0 );
        _w.Write( ( uint )0 );

        // Index buffer size TODO
        //for( var i = 0; i < 3; i++ ) { _w.Write( ( uint )0 ); }
        _w.Write( _indexBufferSize );
        _w.Write( ( uint )0 );
        _w.Write( ( uint )0 );

        _w.Write( ( byte )3 );                             // LOD TODO
        _w.Write( origHeader.EnableIndexBufferStreaming ); // Enable index buffer streaming
        _w.Write( origHeader.EnableEdgeGeometry );         // Enable edge geometry
        _w.Write( ( byte )0 );                             // Padding
    }

    private void WriteModelHeader() {
        var origHeader = _origModel.File!.ModelHeader;

        _w.Write( origHeader.Radius );
        _w.Write( ( ushort )_meshes.Count );
        _w.Write( origHeader.AttributeCount );
        _w.Write( ( ushort )_meshes.Sum( x => x.Value.Count( y => y.Key != -1 ) ) );
        _w.Write( origHeader.MaterialCount );
        _w.Write( origHeader.BoneCount );
        _w.Write( origHeader.BoneTableCount );
        _w.Write( origHeader.ShapeCount );
        _w.Write( origHeader.ShapeMeshCount );
        _w.Write( origHeader.ShapeValueCount );
        _w.Write( origHeader.LodCount );

        // Flags are private, so we need to do this - ugly
        _w.Write( ( byte )( origHeader.DustOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.SnowOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.RainOcclusionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.Unknown1 ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.BgLightingReflectionEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.WavingAnimationDisabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.LightShadowDisabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ShadowDisabled ? 1 : 0 ) );

        _w.Write( origHeader.ElementIdCount );
        _w.Write( origHeader.TerrainShadowMeshCount );

        _w.Write( ( byte )( origHeader.Unknown2 ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.BgUvScrollEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.EnableForceNonResident ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ExtraLodEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ShadowMaskEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.ForceLodRangeEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.EdgeGeometryEnabled ? 1 : 0 ) );
        _w.Write( ( byte )( origHeader.Unknown3 ? 1 : 0 ) );

        _w.Write( origHeader.ModelClipOutDistance );
        _w.Write( origHeader.ShadowClipOutDistance );
        _w.Write( origHeader.Unknown4 );
        _w.Write( origHeader.TerrainShadowSubmeshCount );
        _w.Write( ( byte )0 ); // ??? why is that private in lumina
        _w.Write( origHeader.BGChangeMaterialIndex );
        _w.Write( origHeader.BGCrestChangeMaterialIndex );
        _w.Write( origHeader.Unknown6 );
        _w.Write( origHeader.Unknown7 );
        _w.Write( origHeader.Unknown8 );
        _w.Write( origHeader.Unknown9 );

        _w.Seek( 6, SeekOrigin.Current );
    }

    private List<byte> GetVertexData( MeshPrimitive primitive, int index, Vertex.VertexType type, Vertex.VertexUsage usage, Dictionary<int, int>? blendIndexDict = null ) {
        Vector4 vector4 = new Vector4( 0, 0, 0, 0 );
        switch( usage ) {
            case Vertex.VertexUsage.Position:
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                if( positions != null && positions.Count > index ) {
                    vector4 = new Vector4( positions[index], 1 );
                }
                break;
            case Vertex.VertexUsage.BlendWeights:
                var blendWeights = primitive.GetVertexAccessor( "WEIGHTS_0" )?.AsVector4Array();
                if( blendWeights != null && blendWeights.Count > index ) {
                    vector4 = blendWeights[index];
                }
                break;
            case Vertex.VertexUsage.BlendIndices:
                var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
                // Seems like SE may swap around BlendIndices if an BlendIndex zero appears not in the 0-th index and has more than one weight
                // e.g. would-be: 1, 0 (has weight), _, _ => 0, 1, _, _
                if( blendIndices != null && blendIndices.Count > index ) {
                    vector4 = blendIndices[index];
                    if( blendIndexDict != null ) {
                        for( var i = 0; i < 4; i++ ) {
                            if( blendIndexDict.ContainsKey( ( int )blendIndices[index][i] ) ) {
                                vector4[i] = blendIndexDict[( int )blendIndices[index][i]];
                            }
                        }
                        //PluginLog.Debug( "{a} - {b}", blendIndices[index], vector4 );
                    }
                }
                break;
            case Vertex.VertexUsage.Normal:
                var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
                if( normals != null && normals.Count > index ) {
                    vector4 = new Vector4( normals[index], 0 );
                }
                break;
            case Vertex.VertexUsage.UV:
                var texCoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array();
                if( texCoords?.Count > index ) {
                    vector4 = new( texCoords[index], -1, 2 );
                }
                break;
            case Vertex.VertexUsage.Tangent2:
                // ??
                vector4 = new( 0, 0, 0, 0 );
                break;
            case Vertex.VertexUsage.Tangent1:
                var tangents = primitive.GetVertexAccessor( "TANGENT" )?.AsVector4Array();
                if( tangents?.Count > index ) {
                    vector4 = tangents[index];
                }
                break;
            case Vertex.VertexUsage.Color:
                var colors = primitive.GetVertexAccessor( "COLOR_0" )?.AsVector4Array();
                if( colors?.Count > index ) {
                    vector4 = colors[index];
                }
                break;
        }

        var ret = new List<byte>();
        switch( type ) {
            case Vertex.VertexType.Single3:
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.Z ) );
                break;
            case Vertex.VertexType.Single4:
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.Z ) );
                ret.AddRange( BitConverter.GetBytes( ( float )vector4.W ) );
                break;
            case Vertex.VertexType.UInt:
                ret.Add( ( byte )vector4.X );
                ret.Add( ( byte )vector4.Y );
                ret.Add( ( byte )vector4.Z );
                ret.Add( ( byte )vector4.W );
                break;
            case Vertex.VertexType.ByteFloat4:
                ret.Add( ( byte )( vector4.X * 255f ) );
                ret.Add( ( byte )( vector4.Y * 255f ) );
                ret.Add( ( byte )( vector4.Z * 255f ) );
                ret.Add( ( byte )( vector4.W * 255f ) );
                break;
            case Vertex.VertexType.Half2:
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                break;
            case Vertex.VertexType.Half4:
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Z ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.W ) );

                break;
        }
        return ret;
    }

    private Dictionary<int, List<byte>> WriteVertexData( Mesh mesh, MdlStructs.VertexDeclarationStruct[] vertexDeclarations, Dictionary<int, int>? blendIndexDict = null ) {
        var streams = new Dictionary<int, List<byte>>();
        // TODO: How to handle more than one VertexDeclarationStruct in VertexDeclarations? Does this happen?

        foreach( var primitive in mesh.Primitives ) {
            var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();

            if( positions != null ) {
                for( var vertexId = 0; vertexId < positions.Count; vertexId++ ) {
                    var dec = vertexDeclarations[0];

                    for( var decId = 0; decId < vertexDeclarations[0].VertexElements.Length; decId++ ) {
                        var ve = dec.VertexElements[decId];
                        if( ve.Stream == 255 ) {
                            break;
                        }
                        if( !streams.ContainsKey( ve.Stream ) ) {
                            streams.Add( ve.Stream, new List<byte>() );
                        }
                        var currStream = streams[ve.Stream];
                        currStream.AddRange( GetVertexData( primitive, vertexId, ( Vertex.VertexType )ve.Type, ( Vertex.VertexUsage )ve.Usage, blendIndexDict ) );
                    }
                }
            }
        }

        return streams;
    }

    private MdlStructs.VertexDeclarationStruct[] GetVertexDeclarationStructs() {
        // TODO: Hard-coded?
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

        return new MdlStructs.VertexDeclarationStruct[] { declaration };
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

    private void WriteVertexDeclarations( MdlStructs.VertexDeclarationStruct[] declarations ) {
        foreach( var declaration in declarations ) {
            foreach( var vertexElement in declaration.VertexElements ) {
                _w.Write( vertexElement.Stream );
                _w.Write( vertexElement.Offset );
                _w.Write( vertexElement.Type );
                _w.Write( vertexElement.Usage );
                _w.Write( vertexElement.UsageIndex );
                _w.Write( ( byte )0 );
                _w.Write( ( Half )0 );
            }
        }
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

    public void WriteAll() {
        try {
            var allBones = new List<string>();
            var skeleton = _root.LogicalSkins?[0];
            if( skeleton != null ) {
                PluginLog.Debug( "Number of joints: " + skeleton.JointsCount );

                for( var id = 0; id < skeleton.JointsCount; id++ ) {
                    var (Joint, InverseBindMatrix) = skeleton.GetJoint( id );
                    if( Joint.VisualChildren != null && Joint.VisualChildren.Any() ) {
                        //PluginLog.Debug( "{name} {fd} - {num}", Joint.Name, Joint.LogicalIndex, Joint.VisualChildren.Count());
                    }
                    var boneString = Joint.Name;
                    if( !String.IsNullOrEmpty( boneString ) ) {
                        allBones.Add( boneString );
                    }
                }
            }

            var originalBoneIndexToString = new SortedDictionary<int, string>();
            var Meshes = new List<MdlStructs.MeshStruct>();
            var Submeshes = new List<MdlStructs.SubmeshStruct>();
            var materials = new List<string>();

            var minX = 9999f;
            var minY = 9999f;
            var minZ = 9999f;
            var maxX = -9999f;
            var maxY = -9999f;
            var maxZ = -9999f;

            var shapeNames = new Dictionary<(int, int), List<string>>();

            var vertexCount = 0;
            var indexCount = 0;

            var addedVertices = 0;


            foreach( var meshIndex in _meshes.Keys ) {

                // Get Shape Names
                foreach( var submeshIndex in _meshes[meshIndex].Keys ) {
                    var submeshShapes = new List<string>();
                    var mesh = _meshes[meshIndex][submeshIndex];

                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            foreach( var n in names ) {

                                if( !shapeNames.ContainsKey( (meshIndex, submeshIndex) ) ) {
                                    shapeNames.Add( (meshIndex, submeshIndex), new() );
                                }

                                if( n.ToString().StartsWith( "shp_" ) && !shapeNames[(meshIndex, submeshIndex)].Contains( n.ToString() ) ) {
                                    shapeNames[(meshIndex, submeshIndex)].Add( n.ToString() );
                                    submeshShapes.Add( n.ToString() );
                                }
                            }
                        }
                    }
                    else {
                        PluginLog.Debug( "Mesh contained no extras." );
                    }

                    var primitiveIndexCount = 0;
                    // TODO: DO I REALLY NEED TO LOOP OVER ALL PRIMITIVES?
                    foreach( var primitive in mesh.Primitives ) {
                        var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
                        var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                        var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();

                        PluginLog.Debug( "pos: {val}", positions.Count );

                        var differentElements = new List<int>();

                        PluginLog.Debug( "morphtargetcount: {x}", primitive.MorphTargetsCount );

                        // Get shape vertices that actually have a different position
                        if( submeshShapes.Count > 0 && primitive.MorphTargetsCount == submeshShapes.Count ) {
                            for( var i = 0; i < primitive.MorphTargetsCount; i++ ) {
                                var shape = primitive.GetMorphTargetAccessors( i );
                                var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
                                var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

                                var shapePositions = positionsAccessor?.AsVector3Array();
                                var shapeNormals = normalsAccessor?.AsVector3Array();

                                // if shapePositions != ogPositions && shapeNormals != ogNormals
                                if( shapePositions != null && shapeNormals != null && positions != null && normals != null ) {
                                    for( var j = 0; j < positions.Count; j++ ) {
                                        if( shapePositions[j] != Vector3.Zero ) {
                                            differentElements.Add( j );
                                        }
                                    }
                                }
                            }
                        }

                        // Get the bones that are actually used
                        if( blendIndices != null ) {
                            foreach( var blendIndex in blendIndices ) {
                                for( var i = 0; i < 4; i++ ) {
                                    var val = ( int )blendIndex[i];
                                    // I guess we exclude "n_root"
                                    if( !originalBoneIndexToString.ContainsKey( val ) && val < allBones.Count && allBones[val] != "n_root" ) {
                                        originalBoneIndexToString.Add( val, allBones[val] );
                                    }
                                }
                            }
                        }

                        if( positions != null ) {
                            vertexCount += positions.Count;

                            // Calculate bounding boxes
                            foreach( var pos in positions ) {
                                minX = minX < pos.X ? minX : pos.X;
                                minY = minY < pos.Y ? minY : pos.Y;
                                minZ = minZ < pos.Z ? minZ : pos.Z;

                                maxX = maxX > pos.X ? maxX : pos.X;
                                maxY = maxY > pos.Y ? maxY : pos.Y;
                                maxZ = maxZ > pos.Z ? maxZ : pos.Z;
                            }
                        }

                        var indices = primitive.GetIndexAccessor()?.AsScalarArray();

                        //PluginLog.Debug( "{meshIndex}-{submeshIndex}: #indices: {val}", meshIndex, submeshIndex, indices.Count );
                        var ShapeValueStructs = new List<MdlStructs.ShapeValueStruct>();


                        // TODO: Create a Dictionary<(MeshIndex, SubmeshIndex, ShapeName), List of ShapeValueStructs> ? Or something like that?
                        if( indices != null ) {
                            for( var indexIdx = 0; indexIdx < indices.Count; indexIdx++ ) {
                                var index = indices[indexIdx];
                                if( differentElements.Contains( ( int )index ) ) {
                                    ShapeValueStructs.Add( new() {
                                        BaseIndicesIndex = ( ushort )( indexIdx + indexCount ),
                                        ReplacingVertexIndex = ( ushort )( differentElements.IndexOf( ( int )index ) + addedVertices ) // Later, will need to add this number to the total number of vertices (not including shapes)
                                    } );
                                }
                            }
                            primitiveIndexCount += indices.Count;
                        }

                        // Not too sure how important it is
                        // But ShapeValueStructs appear to be sorted first based on the ReplacingVertexIndex
                        // And if those are equal, then by the BaseIndincesIndex
                        ShapeValueStructs.Sort( CompareShapeValueStructs );
                        foreach( var svs in ShapeValueStructs ) {
                            PluginLog.Debug( "Offset-Value: {off}-{val}", svs.BaseIndicesIndex, svs.ReplacingVertexIndex );
                        }

                        addedVertices += differentElements.Count;

                        // Get materials
                        var material = primitive.Material;
                        // TODO: Ignore "skin" materials?
                        if( !String.IsNullOrEmpty( material.Name ) && !materials.Contains( material.Name ) ) {
                            materials.Add( material.Name );
                        }
                    }

                    var currSubmeshStruct = new MdlStructs.SubmeshStruct() {
                        IndexOffset = 0, // TODO: Back-fill in IndexOffset
                        IndexCount = ( uint )primitiveIndexCount,
                        AttributeIndexMask = 0,  // TODO: AttributeIndexMask
                        BoneStartIndex = 0, // TODO: BoneStartIndex
                        BoneCount = 0   // TODO: BoneCount for submesh
                    };

                    Submeshes.Add( currSubmeshStruct );
                    indexCount += primitiveIndexCount;
                }

                var currMeshStruct = new MdlStructs.MeshStruct() {
                    VertexCount = ( ushort )vertexCount,
                    IndexCount = ( uint )indexCount,
                    MaterialIndex = 0,  //TODO: MaterialIndex
                    SubMeshIndex = 0, // TODO: What is SubMeshIndex?
                    SubMeshCount = ( ushort )_meshes[meshIndex].Keys.Count,
                    BoneTableIndex = 0, // TODO: BoneTableIndex
                    StartIndex = 0, // TODO: StartIndex
                    VertexBufferOffset = new uint[3] { 0, 0, 0 },   // TODO: Back-fill in VertexBufferOffset
                    VertexBufferStride = new byte[3] { 0, 0, 0 },   // TODO: Back-fill in VertexBufferStride
                    VertexStreamCount = 2   // Always two? Probably goes back to VertexDeclarations...
                };

                Meshes.Add( currMeshStruct );
            }

            // Bones in order of parent-children hierarchy
            var sortedBones = new List<string>();
            if( skeleton != null ) {
                sortedBones = GetJoints( skeleton.GetJoint( 0 ).Joint.VisualChildren.ToList(), originalBoneIndexToString.Values.ToList() );
            }
            var originalBoneIndexToNewIndex = new Dictionary<int, int>();
            foreach( var kvp in originalBoneIndexToString ) {
                var index = sortedBones.IndexOf( kvp.Value );
                if( index != -1 ) {
                    originalBoneIndexToNewIndex.Add( kvp.Key, index );
                }
            }

            // for strings, i guess the bone names are alphabetized
            // so this maps the bone index from the used and hierarchy-ordered list
            // to the index in the alphabetized list
            var boneTable = new List<int>();
            var BoneTables = new List<List<int>>();
            BoneTables.Add( new List<int>() );
            var values = originalBoneIndexToString.Values.Where( x => x != "n_root" ).ToList();
            var boneTableIndex = 0;

            foreach( var bone in sortedBones ) {
                var idx = values.IndexOf( bone );
                if( idx != -1 ) {
                    // TODO: I think we'd have more than one BoneTable if the mesh requires more than 64 bones
                    // However, how do we divvy them up into multiple BoneTables?
                    // Because each mesh can only have at most 64 bones
                    // But if two meshes share bones across two BoneTables... So one BoneTable per mesh?
                    // And if we have multiple BoneTables... wouldn't that change BlendIndices?
                    if( BoneTables[boneTableIndex].Count > 64 ) {
                        boneTableIndex++;
                        BoneTables.Add( new List<int>() );
                    }
                    BoneTables[boneTableIndex].Add( idx );
                }
            }

            var Strings = new List<string>();
            var stringsAsChar = new List<char>();
            foreach( var str in Strings ) {
                var charArr = str.ToCharArray();
                stringsAsChar.AddRange( charArr );
                stringsAsChar.Add( '\0' );
            }
            var BoneNameOffsets = new List<int>();
            var MaterialNameOffsets = new List<int>();

            // TODO: add/edit attributes
            Strings.Add( "atr_leg" );
            stringsAsChar.AddRange( "atr_leg\0" );

            // Write Bones
            foreach( var boneName in originalBoneIndexToString.Values.Where( x => x != "n_root" ) ) {
                BoneNameOffsets.Add( stringsAsChar.Count );
                Strings.Add( boneName );

                stringsAsChar.AddRange( boneName );
                stringsAsChar.Add( '\0' );
            }

            // TODO: add/edit materials
            foreach( var material in materials ) {
                MaterialNameOffsets.Add( stringsAsChar.Count );

                Strings.Add( material );
                stringsAsChar.AddRange( material );
                stringsAsChar.Add( '\0' );
            }

            // TODO: Shape names
            var distinctNames = new List<string>();
            foreach( var shapeList in shapeNames.Values ) {
                foreach( var val in shapeList ) {
                    if( !distinctNames.Contains( val ) ) {
                        distinctNames.Add( val );
                    }
                }
            }
            foreach( var name in distinctNames ) {
                Strings.Add( name );
                stringsAsChar.AddRange( name );
                stringsAsChar.Add( '\0' );
            }

            // TODO?: extra paths
            var VertexDeclarationStructs = GetVertexDeclarationStructs();
            var vertexData = new List<byte>();
            var indexData = new List<byte>();

            foreach( var meshIndex in _meshes.Keys ) {
                foreach( var submeshIndex in _meshes[meshIndex].Keys ) {
                    var mesh = _meshes[meshIndex][submeshIndex];
                    var dict = WriteVertexData( mesh, VertexDeclarationStructs, originalBoneIndexToNewIndex );

                    foreach( var val in dict.Values ) {
                        vertexData.AddRange( val );
                    }

                    foreach( var primitive in mesh.Primitives ) {
                        var indices = primitive.GetIndices();
                        foreach( var index in indices ) {
                            indexData.AddRange( BitConverter.GetBytes( ( ushort )index ) );
                        }
                    }
                }
            }
            _vertexBufferSize = ( uint )( vertexData.Count );
            _indexBufferSize = ( uint )( indexData.Count );
        }
        catch( Exception ex ) {
            PluginLog.Debug( ex.ToString() );
        }
        finally {
            PluginLog.Debug( "End" );
        }
    }

    public void Dispose() {
        _w.Dispose();
    }
}