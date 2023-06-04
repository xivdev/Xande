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

    private List<byte> _vertexData;
    private List<byte> _indexData;

    private Dictionary<(int meshIndex, int submeshIndex, string shapeName), List<MdlStructs.ShapeValueStruct>> _shapeData = new();

    private MdlFile _file;

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

    private List<byte> GetVertexData( MeshPrimitive primitive, int index, Vertex.VertexType type, Vertex.VertexUsage usage, Dictionary<int, int>? blendIndexDict = null ) {
        Vector4 vector4 = new Vector4( 0, 0, 0, 0 );
        string str = "";
        switch( usage ) {
            case Vertex.VertexUsage.Position:
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                if( positions != null && positions.Count > index ) {
                    vector4 = new Vector4( positions[index], 1 );
                }
                str += "position ";

                break;
            case Vertex.VertexUsage.BlendWeights:
                var blendWeights = primitive.GetVertexAccessor( "WEIGHTS_0" )?.AsVector4Array();
                if( blendWeights != null && blendWeights.Count > index ) {
                    vector4 = blendWeights[index];
                }
                str += "blendweight ";
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
                    str += "blendindices ";
                }
                break;
            case Vertex.VertexUsage.Normal:
                var normals = primitive.GetVertexAccessor( "NORMAL" )?.AsVector3Array();
                if( normals != null && normals.Count > index ) {
                    vector4 = new Vector4( normals[index], 0 );
                }
                str += "normal ";
                break;
            case Vertex.VertexUsage.UV:
                var texCoords = primitive.GetVertexAccessor( "TEXCOORD_0" )?.AsVector2Array();
                if( texCoords?.Count > index ) {
                    vector4 = new( texCoords[index].X, texCoords[index].Y - 1, -1, 2 );
                    //PluginLog.Debug( "UV: {x}", vector4 );
                }
                str += "uv ";
                break;
            case Vertex.VertexUsage.Tangent2:
                // ??
                vector4 = new( 0, 0, 0, 0 );
                str += "tangent2 ";
                break;
            case Vertex.VertexUsage.Tangent1:
                var tangents = primitive.GetVertexAccessor( "TANGENT" )?.AsVector4Array();
                if( tangents?.Count > index ) {
                    vector4 = tangents[index];
                }
                str += "tangent1 ";
                break;
            case Vertex.VertexUsage.Color:
                var colors = primitive.GetVertexAccessor( "COLOR_0" )?.AsVector4Array();
                if( colors?.Count > index ) {
                    vector4 = colors[index];
                }
                str += "color ";
                break;
        }

        var ret = new List<byte>();
        switch( type ) {
            case Vertex.VertexType.Single3:
                ret.AddRange( BitConverter.GetBytes( vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( vector4.Z ) );
                str += "single3";
                break;
            case Vertex.VertexType.Single4:
                ret.AddRange( BitConverter.GetBytes( vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( vector4.Z ) );
                ret.AddRange( BitConverter.GetBytes( vector4.W ) );
                str += "single4";
                break;
            case Vertex.VertexType.UInt:
                ret.Add( ( byte )vector4.X );
                ret.Add( ( byte )vector4.Y );
                ret.Add( ( byte )vector4.Z );
                ret.Add( ( byte )vector4.W );
                str += "uint";
                break;
            case Vertex.VertexType.ByteFloat4:
                ret.Add( ( byte )( vector4.X * 255f ) );
                ret.Add( ( byte )( vector4.Y * 255f ) );
                ret.Add( ( byte )( vector4.Z * 255f ) );
                ret.Add( ( byte )( vector4.W * 255f ) );
                str += "bytefloat";
                break;
            case Vertex.VertexType.Half2:
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                str += "half2";
                break;
            case Vertex.VertexType.Half4:
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.X ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Y ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.Z ) );
                ret.AddRange( BitConverter.GetBytes( ( Half )vector4.W ) );
                str += "half4";
                break;
        }
        return ret;
    }

    /// <summary>
    /// Key: StreamIndex
    /// Value: List of bytes
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="vertexDeclarations"></param>
    /// <param name="blendIndexDict"></param>
    /// <returns></returns>
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

            var file = new MdlFile();
            var ElementIdStructs = new MdlStructs.ElementIdStruct[0];



            var allBones = new List<string>();
            var skeleton = _root.LogicalSkins?[0];
            if( skeleton != null ) {
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
                foreach( var submeshIndex in _meshes[meshIndex].Keys ) {
                    var mesh = _meshes[meshIndex][submeshIndex];
                    foreach( var primitive in mesh.Primitives ) {
                        var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                        if( positions != null ) {
                            vertexCount += positions.Count;
                        }
                    }
                }
            }

            foreach( var meshIndex in _meshes.Keys ) {
                // Get Shape Names
                var materialIndex = -1;
                var submeshBoneCount = 0;
                var boneStartIndex = 0;

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

                        var differentElements = new List<int>();

                        // Get shape vertices that actually have a different position
                        if( submeshShapes.Count > 0 && primitive.MorphTargetsCount == submeshShapes.Count ) {
                            for( var i = 0; i < primitive.MorphTargetsCount; i++ ) {
                                var shape = primitive.GetMorphTargetAccessors( i );
                                var hasPositions = shape.TryGetValue( "POSITION", out var positionsAccessor );
                                var hasNormals = shape.TryGetValue( "NORMAL", out var normalsAccessor );

                                var shapePositions = positionsAccessor?.AsVector3Array();
                                var shapeNormals = normalsAccessor?.AsVector3Array();

                                if( shapePositions != null && shapeNormals != null && positions != null && normals != null ) {
                                    for( var j = 0; j < shapePositions.Count; j++ ) {
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
                                    // I guess we exclude "n_root"? And "n_hara" ???
                                    if( !originalBoneIndexToString.ContainsKey( val ) && val < allBones.Count
                                        && allBones[val] != "n_root" && allBones[val] != "n_hara" ) {
                                        originalBoneIndexToString.Add( val, allBones[val] );
                                        submeshBoneCount++;
                                    }
                                }
                            }
                        }

                        if( positions != null ) {
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
                                        ReplacingVertexIndex = ( ushort )( differentElements.IndexOf( ( int )index ) + vertexCount + addedVertices ) // Later, will need to add this number to the total number of vertices (not including shapes)
                                    } );
                                }
                            }
                            primitiveIndexCount += indices.Count;
                        }

                        // Not too sure how important it is
                        // But ShapeValueStructs appear to be sorted first based on the ReplacingVertexIndex
                        // And if those are equal, then by the BaseIndincesIndex
                        ShapeValueStructs.Sort( CompareShapeValueStructs );
                        /*
                        foreach( var svs in ShapeValueStructs ) {
                            PluginLog.Debug( "Offset-Value: {off}-{val}", svs.BaseIndicesIndex, svs.ReplacingVertexIndex );
                        }
                        */

                        addedVertices += differentElements.Count;

                        // Get materials
                        var material = primitive.Material;
                        // TODO: Ignore "skin" materials?
                        if( !String.IsNullOrEmpty( material.Name ) && !materials.Contains( material.Name ) ) {
                            materials.Add( material.Name );
                            if( materialIndex == -1 ) {
                                materialIndex = materials.IndexOf( material.Name );
                            }
                        }
                    }

                    var currSubmeshStruct = new MdlStructs.SubmeshStruct() {
                        IndexOffset = ( uint )indexCount,
                        IndexCount = ( uint )primitiveIndexCount,
                        AttributeIndexMask = 0,  // TODO: AttributeIndexMask
                        BoneStartIndex = ( ushort )boneStartIndex, // TODO: BoneStartIndex
                        BoneCount = ( ushort )submeshBoneCount   // TODO: BoneCount for submesh
                    };

                    Submeshes.Add( currSubmeshStruct );
                    indexCount += primitiveIndexCount;
                    boneStartIndex += submeshBoneCount;
                }

                var currMeshStruct = new MdlStructs.MeshStruct() {
                    VertexCount = ( ushort )vertexCount,
                    IndexCount = ( uint )indexCount,
                    MaterialIndex = ( ushort )materialIndex,  //TODO: MaterialIndex
                    SubMeshIndex = 0, // TODO: What is SubMeshIndex?
                    SubMeshCount = ( ushort )_meshes[meshIndex].Keys.Count,
                    BoneTableIndex = 0, // TODO: BoneTableIndex
                    StartIndex = 0, // TODO: StartIndex
                    VertexBufferOffset = new uint[3] { 0, 0, 0 },   // To be filled in later
                    VertexBufferStride = new byte[3] { 0, 0, 0 },   // To be filled in later
                    VertexStreamCount = 2   // Always two? Probably goes back to VertexDeclarations...
                };

                Meshes.Add( currMeshStruct );
            }

            PluginLog.Debug( "Submesh size: {x}", Submeshes.Count );

            var filledBoundingBoxStruct = new MdlStructs.BoundingBoxStruct() {
                Min = new[] { minX, minY, minZ, -9999f },    // TODO: BoundingBoxStruct "w" values?
                Max = new[] { maxX, maxY, maxZ, 9999f }
            };
            var zeroBoundingBoxStruct = new MdlStructs.BoundingBoxStruct() {
                Min = new[] { 0f, 0, 0, 0 },
                Max = new[] { 0f, 0, 0, 0 }
            };
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
            var BoneTables = new List<ushort[]>();
            var BoneTableBoneCounts = new List<int>() { 0 };
            BoneTables.Add( new ushort[64] );
            var values = originalBoneIndexToString.Values.Where( x => x != "n_root" ).ToList();
            values.Sort();

            var boneTableIndex = 0;

            var otherindex = 0;
            foreach( var bone in sortedBones ) {
                var idx = values.IndexOf( bone );
                if( idx != -1 ) {
                    // TODO: I think we'd have more than one BoneTable if the mesh requires more than 64 bones
                    // However, how do we divvy them up into multiple BoneTables?
                    // Because each mesh can only have at most 64 bones
                    // But if two meshes share bones across two BoneTables... So one BoneTable per mesh?
                    // And if we have multiple BoneTables... wouldn't that change BlendIndices?
                    /*
                    if( BoneTables[boneTableIndex].Count > 64 ) {
                        boneTableIndex++;
                        BoneTables.Add( new List<int>() );
                    }
                    */

                    BoneTables[boneTableIndex][otherindex] = ( ushort )idx;
                    BoneTableBoneCounts[boneTableIndex]++;
                    otherindex++;
                }
            }

            var BoneTableStructs = new List<MdlStructs.BoneTableStruct>();
            BoneTableStructs.Add(
                new() {
                    BoneIndex = BoneTables[0],
                    BoneCount = ( byte )BoneTableBoneCounts[0]
                } );

            // TODO: SubmeshBoneMap
            var SubmeshBoneMap = new ushort[BoneTableBoneCounts[0]];
            for( var i = 0; i < SubmeshBoneMap.Length; i++ ) {
                SubmeshBoneMap[i] = ( ushort )i;
            }


            var Strings = new List<string>();
            var stringsAsChar = new List<char>();
            foreach( var str in Strings ) {
                var charArr = str.ToCharArray();
                stringsAsChar.AddRange( charArr );
                stringsAsChar.Add( '\0' );
            }
            var AttributeNameOffsets = new List<int>();
            var BoneNameOffsets = new List<int>();
            var MaterialNameOffsets = new List<int>();

            // TODO: add/edit attributes
            /*
            AttributeNameOffsets.Add( 0 );
            Strings.Add( "atr_leg" );
            stringsAsChar.AddRange( "atr_leg\0" );
            */

            // Write Bones
            //foreach( var boneName in originalBoneIndexToString.Values.Where( x => x != "n_root" && x != "n_hara" ) ) {
            foreach( var boneName in values ) {
                BoneNameOffsets.Add( stringsAsChar.Count );
                Strings.Add( boneName );

                stringsAsChar.AddRange( boneName );
                stringsAsChar.Add( '\0' );
            }

            // TODO: add/edit materials
            foreach( var material in materials ) {
                MaterialNameOffsets.Add( stringsAsChar.Count );

                Strings.Add( "/" + material + ".mtrl" );
                stringsAsChar.AddRange( "/" + material + ".mtrl" );
                stringsAsChar.Add( '\0' );
            }

            stringsAsChar.Add( '\0' );
            stringsAsChar.Add( '\0' );


            // TODO: Shape names
            /*
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
            */

            foreach( var str in Strings ) {
                PluginLog.Debug( str );
            }

            // TODO?: extra paths
            var VertexDeclarationStructs = GetVertexDeclarationStructs();
            _vertexData = new List<byte>();
            _indexData = new List<byte>();
            var vertexDict = new Dictionary<int, List<byte>>();

            foreach( var meshIndex in _meshes.Keys ) {
                foreach( var submeshIndex in _meshes[meshIndex].Keys ) {
                    var mesh = _meshes[meshIndex][submeshIndex];
                    var submeshVertices = WriteVertexData( mesh, VertexDeclarationStructs, originalBoneIndexToNewIndex );

                    /*
                    foreach( var val in submeshVertices.Values ) {
                        _vertexData.AddRange( val );
                    }
                    */
                    for( var i = 0; i < submeshVertices.Count; i++ ) {
                        if( !vertexDict.ContainsKey( i ) ) {
                            vertexDict.Add( i, new List<byte>() );
                        }
                        vertexDict[i].AddRange( submeshVertices[i] );
                    }

                    foreach( var primitive in mesh.Primitives ) {
                        var indices = primitive.GetIndices();
                        foreach( var index in indices ) {
                            _indexData.AddRange( BitConverter.GetBytes( ( ushort )index ) );
                        }
                    }
                }
            }

            var done = false;
            foreach( var data in vertexDict.Values ) {


                _vertexData.AddRange( data );
            }

            _vertexBufferSize = ( uint )( _vertexData.Count );
            _indexBufferSize = ( uint )( _indexData.Count );

            // Finalize?
            file.FileHeader = new() {
                Version = _origModel.File?.FileHeader.Version ?? 16777220,
                StackSize = 0,   // To-be filled in later
                RuntimeSize = 0,    // To be filled later
                VertexDeclarationCount = ( ushort )VertexDeclarationStructs.Length,
                MaterialCount = ( ushort )materials.Count,
                VertexOffset = new uint[] { 0, 0, 0 },
                IndexOffset = new uint[] { 0, 0, 0 },
                VertexBufferSize = new uint[] { _vertexBufferSize, 0, 0 },
                IndexBufferSize = new uint[] { _indexBufferSize, 0, 0 },
                LodCount = 1,
                EnableIndexBufferStreaming = _origModel.File?.FileHeader.EnableIndexBufferStreaming ?? true,
                EnableEdgeGeometry = _origModel.File?.FileHeader.EnableEdgeGeometry ?? false
            };
            file.VertexDeclarations = VertexDeclarationStructs;
            file.StringCount = ( ushort )Strings.Count;
            file.Strings = stringsAsChar.ConvertAll( x => ( byte )x ).ToArray();
            file.ModelHeader = new() {
                Radius = _origModel.File.ModelHeader.Radius,
                MeshCount = ( ushort )Meshes.Count,
                AttributeCount = ( ushort )AttributeNameOffsets.Count,
                SubmeshCount = ( ushort )Submeshes.Count,
                MaterialCount = ( ushort )materials.Count,
                BoneCount = ( ushort )BoneNameOffsets.Count,
                BoneTableCount = ( ushort )BoneTables.Count,

                ShapeCount = 0,
                ShapeValueCount = 0, //(ushort)_shapeData.Values.Count
                LodCount = 1,
                ElementIdCount = _origModel.File?.ModelHeader.ElementIdCount ?? 0,
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
            file.ElementIds = ElementIdStructs;
            file.Lods = GetLodStruct( ( ushort )_meshes.Count );

            if( file.ModelHeader.ExtraLodEnabled ) {
                // ExtraLods...
            }
            file.Meshes = Meshes.ToArray();
            file.AttributeNameOffsets = AttributeNameOffsets.ConvertAll( x => ( uint )x ).ToArray();
            file.TerrainShadowMeshes = new MdlStructs.TerrainShadowMeshStruct[0];
            file.Submeshes = Submeshes.ToArray();
            file.TerrainShadowSubmeshes = new MdlStructs.TerrainShadowSubmeshStruct[0];

            file.MaterialNameOffsets = MaterialNameOffsets.ConvertAll( x => ( uint )x ).ToArray();
            file.BoneNameOffsets = BoneNameOffsets.ConvertAll( x => ( uint )x ).ToArray();
            file.BoneTables = BoneTableStructs.ToArray();

            // TODO: Shape structs
            file.Shapes = new MdlStructs.ShapeStruct[0];
            file.ShapeMeshes = new MdlStructs.ShapeMeshStruct[0];
            file.ShapeValues = new MdlStructs.ShapeValueStruct[0];

            file.SubmeshBoneMap = SubmeshBoneMap;

            // TODO: Bounding boxes? Specifically WaterBoundingBoxes and VerticalFogBoundingBoxes?
            file.BoundingBoxes = filledBoundingBoxStruct;
            file.ModelBoundingBoxes = filledBoundingBoxStruct;
            file.WaterBoundingBoxes = zeroBoundingBoxStruct;
            file.VerticalFogBoundingBoxes = zeroBoundingBoxStruct;

            file.BoneBoundingBoxes = new MdlStructs.BoundingBoxStruct[BoneNameOffsets.Count];
            for( var i = 0; i < BoneNameOffsets.Count; i++ ) {
                file.BoneBoundingBoxes[i] = zeroBoundingBoxStruct;
            }

            FillInValues( file );

            WriteMdlFile( file );
        }
        catch( Exception ex ) {
            PluginLog.Debug( ex.ToString() );
        }
        finally {
            PluginLog.Debug( "End" );
        }
    }

    private MdlStructs.LodStruct[] GetLodStruct( ushort meshCount ) {
        var ret = new MdlStructs.LodStruct[3];
        for( var i = 0; i < 3; i++ ) {
            ret[i] = new() {
                MeshIndex = (ushort)i,
                MeshCount = 0,
                ModelLodRange = float.MaxValue,  // idk
                TextureLodRange = float.MaxValue,   // idk
                WaterMeshIndex = 1, // idk?
                WaterMeshCount = 0,
                ShadowMeshIndex = 1,    //idk?
                ShadowMeshCount = 0,
                TerrainShadowMeshIndex = 0, //idk?
                TerrainShadowMeshCount = 0,
                VerticalFogMeshIndex = (ushort)(i + 1),   //idk?
                VerticalFogMeshCount = 0,
                EdgeGeometrySize = 0,
                EdgeGeometryDataOffset = 0, // To be filled in later
                PolygonCount = 0,
                Unknown1 = 0,
                VertexBufferSize = 0,   // to be filled in later
                IndexBufferSize = 0,    // to be filled in later
                VertexDataOffset = 0,   // to be filled in later
                IndexDataOffset = 0     // to be filled in later
            };

            if( i == 0 ) {
                ret[i].MeshCount = meshCount;
            }
        }
        return ret;
    }


    private void FillInValues( MdlFile file ) {
        // This is all assuming we're putting all the data into the first LoD
        var stackSize = file.VertexDeclarations.Length * 136;
        // TODO: This calculated value still seems to be off
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
            + file.Shapes.Length * 10
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

        file.FileHeader.StackSize = ( uint )stackSize;
        file.FileHeader.RuntimeSize = ( uint )runtimeSize;
        file.FileHeader.VertexOffset[0] = ( uint )vertexOffset0;
        file.FileHeader.IndexOffset[0] = ( uint )indexOffset0;

        file.Lods[0].EdgeGeometryDataOffset = indexOffset0;
        file.Lods[0].VertexDataOffset = ( uint )vertexOffset0;
        file.Lods[0].IndexDataOffset = indexOffset0;

        file.Lods[0].VertexBufferSize = file.FileHeader.VertexBufferSize[0];
        file.Lods[0].IndexBufferSize = file.FileHeader.IndexBufferSize[0];

        var fileSize = indexOffset0 + file.FileHeader.IndexBufferSize[0];
        file.Lods[1].EdgeGeometryDataOffset = fileSize;
        file.Lods[1].VertexDataOffset = fileSize;
        file.Lods[1].IndexDataOffset = fileSize;

        file.Lods[2].EdgeGeometryDataOffset = fileSize;
        file.Lods[2].VertexDataOffset = fileSize;
        file.Lods[2].IndexDataOffset = fileSize;

        file.Meshes[0].VertexBufferOffset[1] = ( uint )20 * file.Meshes[0].VertexCount;   // Currently 20 because that's the size of "stream 0" in vertexdeclarations

        foreach( var mesh in file.Meshes ) {
            mesh.VertexBufferStride[0] = 20;
            mesh.VertexBufferStride[1] = 36;
        }

    }

    public void WriteMdlFile( MdlFile mdlFile ) {
        WriteFileHeader( mdlFile.FileHeader );
        WriteVertexDeclarations( mdlFile.VertexDeclarations );

        _w.Write( mdlFile.StringCount );
        _w.Write( ( ushort )0 );
        _w.Write( ( uint )mdlFile.Strings.Length );
        _w.Write( mdlFile.Strings );

        WriteModelHeader( mdlFile.ModelHeader );
        WriteElementIds( mdlFile.ElementIds );
        WriteLods( mdlFile.Lods );

        if( mdlFile.ModelHeader.ExtraLodEnabled ) {
            // WriteExtraLod
        }

        WriteMeshStructs( mdlFile.Meshes );

        for( var i = 0; i < mdlFile.AttributeNameOffsets.Length; i++ ) {
            _w.Write( mdlFile.AttributeNameOffsets[i] );
        }
        // WriteTerrainShadowMeshes
        WriteSubmeshStructs( mdlFile.Submeshes );
        // TerrainShadowSubmeshes

        for( var i = 0; i < mdlFile.MaterialNameOffsets.Length; i++ ) {
            _w.Write( mdlFile.MaterialNameOffsets[i] );
        }

        for( var i = 0; i < mdlFile.BoneNameOffsets.Length; i++ ) {
            _w.Write( mdlFile.BoneNameOffsets[i] );
        }

        WriteBoneTableStructs( mdlFile.BoneTables );
        // WriteShapeStructs
        // WriteShapeMeshes
        // WriteShapeValues
        var submeshBoneMapSize = mdlFile.SubmeshBoneMap.Length * 2;
        _w.Write( ( uint )submeshBoneMapSize );
        foreach( var val in mdlFile.SubmeshBoneMap ) {
            _w.Write( val );
        };

        _w.Write( ( byte )7 );
        _w.Seek( 7, SeekOrigin.Current );

        WriteBoundingBoxStructs( mdlFile.BoundingBoxes );
        WriteBoundingBoxStructs( mdlFile.ModelBoundingBoxes );
        WriteBoundingBoxStructs( mdlFile.WaterBoundingBoxes );
        WriteBoundingBoxStructs( mdlFile.VerticalFogBoundingBoxes );
        foreach( var boneBoundingBox in mdlFile.BoneBoundingBoxes ) {
            WriteBoundingBoxStructs( boneBoundingBox );
        }

        _w.Write( _vertexData.ToArray() );
        _w.Write( _indexData.ToArray() );

        PluginLog.Debug( "Finished writing" );
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
                modelHeader.WavingAnimationDisabled,
                modelHeader.LightShadowDisabled,
                modelHeader.ShadowDisabled
            }
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
        foreach( var lod in lods ) {
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

    private void WriteSubmeshStructs( MdlStructs.SubmeshStruct[] submeshes ) {
        foreach( var submesh in submeshes ) {
            _w.Write( submesh.IndexOffset );
            _w.Write( submesh.IndexCount );
            _w.Write( submesh.AttributeIndexMask );
            _w.Write( submesh.BoneStartIndex );
            _w.Write( submesh.BoneCount );
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

    private void WriteBoundingBoxStructs( MdlStructs.BoundingBoxStruct bb ) {
        foreach( var minVal in bb.Min ) {
            _w.Write( minVal );
        }
        foreach( var maxVal in bb.Max ) {
            _w.Write( maxVal );
        }
    }

    public void Dispose() {
        _w.Dispose();
    }
}