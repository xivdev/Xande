using System.Drawing;
using System.Numerics;
using Dalamud.Logging;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using Xande.Files;
using Xande.Havok;
using Xande.Models.Export;
using Mesh = Lumina.Models.Models.Mesh;
using Lumina.Data.Files;
using Lumina.Data;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using Lumina;
using Xande.GltfImporter;

// ReSharper disable InconsistentNaming

namespace Xande.Models;

public static class ModelExtensions {
    public static string[]? BoneTable( this Mesh mesh ) {
        var rawMesh = mesh.Parent.File!.Meshes[ mesh.MeshIndex ];
        if( rawMesh.BoneTableIndex == 255 ) { return null; }

        var rawTable = mesh.Parent.File!.BoneTables[ rawMesh.BoneTableIndex ];
        return rawTable.BoneIndex.Take( rawTable.BoneCount ).Select( b => mesh.Parent.StringOffsetToStringMap[ ( int )mesh.Parent.File!.BoneNameOffsets[ b ] ] ).ToArray();
    }

    public static Vertex VertexByIndex( this Mesh mesh, int index ) => mesh.Vertices[ mesh.Indices[ index ] ];
}

public enum ExportModelType {
    UNMODDED,
    DEFAULT,
    PLAYER
}

public class ModelConverter {
    private readonly LuminaManager  _lumina;
    private readonly PbdFile        _pbd;
    private readonly IPathResolver? _pathResolver;

    private Dictionary< string, Lumina.Models.Materials.Material > _materials = new();
    private Dictionary< string, Texture >                          _textures  = new();

    private ILogger? _logger;

    public ModelConverter( LuminaManager lumina, IPathResolver? pathResolver = null, ILogger? logger = null ) {
        _logger       = logger;
        _lumina       = lumina;
        _pbd          = lumina.GetPbdFile();
        _pathResolver = pathResolver;
    }

    /*
    UV null and Color null => Not happening
    UV not null and Color null => VertexTextureN; N = 1 and 2
    UV null and Color not null => VertexColor1
    UV not null and Color not null => VertexColor1TextureN; N = 1 and 2


    null blend indices? Y
    null blend weights? Y

    null normal? Y
    null position? N
    null tangent1? Y
    null tangent2? Y

    null colour? Y
    null uv? Y

    UV != null => Color != null
    UV == null => Color == null


    Normal != null => VertexPositionNormal
    Tangent != null => VertexPositionNormalTangent
    VertexPosition
    */

    private ImageBuilder ComposeTexture( string path, Bitmap bitmap ) {
        bitmap.Save( path );
        var builder = ImageBuilder.From( path );
        builder.AlternateWriteFileName = Path.GetFileName( path );
        return builder;
    }

    private void ComposeTextures( MaterialBuilder glTFMaterial, Lumina.Models.Materials.Material xivMaterial, string outputDir ) {
        var xivTextureMap = new Dictionary< TextureUsage, Bitmap >();

        foreach( var xivTexture in xivMaterial.Textures ) {
            if( xivTexture.TexturePath == "dummy.tex" ) { continue; }
            xivTextureMap.Add( xivTexture.TextureUsageRaw, _lumina.GetTextureBuffer( xivTexture ) );
        }

        if( xivMaterial.ShaderPack == "character.shpk" ) {
            if( xivTextureMap.TryGetValue( TextureUsage.SamplerNormal, out var normal ) ) {
                var diffuse  = ( Bitmap )normal.Clone();
                var specular = ( Bitmap )normal.Clone();
                var emission = ( Bitmap )normal.Clone();

                for( var x = 0; x < normal.Width; x++ ) {
                    for( var y = 0; y < normal.Height; y++ ) {
                        var normalPixel = normal.GetPixel( x, y );

                        //var b = (Math.Clamp(normalPixel.B, (byte)0, (byte)128) * 255) / 128;
                        var colorSetIndex1 = normalPixel.A / 17 * 16;
                        var colorSetBlend  = normalPixel.A % 17 / 17.0;
                        //var colorSetIndex2 = (((normalPixel.A / 17) + 1) % 16) * 16;
                        var colorSetIndexT2 = normalPixel.A / 17;
                        var colorSetIndex2  = ( colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1 ) * 16;

                        normal.SetPixel( x, y, Color.FromArgb( 255, normalPixel.R, normalPixel.G, 255 ) );

                        var diffuseBlendColour = ColorUtility.BlendColorSet( in xivMaterial.File!.ColorSetInfo, colorSetIndex1, colorSetIndex2, normalPixel.B, colorSetBlend,
                                                                             ColorUtility.TextureType.Diffuse );
                        var specularBlendColour = ColorUtility.BlendColorSet( in xivMaterial.File!.ColorSetInfo, colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                                                                              ColorUtility.TextureType.Specular );
                        var emissionBlendColour = ColorUtility.BlendColorSet( in xivMaterial.File!.ColorSetInfo, colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                                                                              ColorUtility.TextureType.Emissive );

                        diffuse.SetPixel( x, y, diffuseBlendColour );
                        specular.SetPixel( x, y, specularBlendColour );
                        emission.SetPixel( x, y, emissionBlendColour );
                    }
                }

                // I don't think this is safe to TryAdd but I saw errors in some models without it
                xivTextureMap.TryAdd( TextureUsage.SamplerDiffuse, diffuse );
                xivTextureMap.TryAdd( TextureUsage.SamplerSpecular, specular );
                xivTextureMap.TryAdd( TextureUsage.SamplerReflection, emission );
            }

            if( xivTextureMap.TryGetValue( TextureUsage.SamplerMask, out var mask ) && xivTextureMap.TryGetValue( TextureUsage.SamplerSpecular, out var specularMap ) ) {
                var occlusion = ( Bitmap )mask.Clone();

                for( var x = 0; x < mask.Width; x++ ) {
                    for( var y = 0; y < mask.Height; y++ ) {
                        var maskPixel     = mask.GetPixel( x, y );
                        var specularPixel = specularMap.GetPixel( x, y );

                        specularMap.SetPixel( x, y, Color.FromArgb(
                                                  specularPixel.A,
                                                  Convert.ToInt32( specularPixel.R * Math.Pow( maskPixel.G / 255.0, 2 ) ),
                                                  Convert.ToInt32( specularPixel.G * Math.Pow( maskPixel.G / 255.0, 2 ) ),
                                                  Convert.ToInt32( specularPixel.B * Math.Pow( maskPixel.G / 255.0, 2 ) )
                                              ) );

                        var occlusionPixel = occlusion.GetPixel( x, y );
                        occlusion.SetPixel( x, y, Color.FromArgb(
                                                255,
                                                occlusionPixel.R,
                                                occlusionPixel.R,
                                                occlusionPixel.R
                                            ) );
                    }
                }

                xivTextureMap.Add( TextureUsage.SamplerWaveMap, occlusion );
            }
        }

        var num = 0;
        foreach( var xivTexture in xivTextureMap ) {
            var name = glTFMaterial.Name
                .Split( "/" )
                .Last()
                .Replace( ".mtrl", "" );
            var    tex = xivTexture.Value;
            string texturePath;
            switch( xivTexture.Key ) {
                case TextureUsage.SamplerColorMap0:
                case TextureUsage.SamplerDiffuse:
                    texturePath = $"{name}_diffuse_{num}.png";
                    glTFMaterial.WithChannelImage( KnownChannel.BaseColor, ComposeTexture( texturePath, tex ) );
                    break;

                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    texturePath = $"{name}_normal_{num}.png";
                    glTFMaterial.WithChannelImage( KnownChannel.Normal, ComposeTexture( texturePath, tex ) );
                    break;

                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    texturePath = $"{name}_specular_{num}.png";
                    //glTFMaterial.WithChannelImage( KnownChannel.SpecularColor, Path.GetFullPath( Path.Combine( outputDir, texturePath ) ) );
                    glTFMaterial.WithSpecularColor( ComposeTexture( texturePath, tex ) );
                    break;

                case TextureUsage.SamplerWaveMap:
                    texturePath = $"{name}_occlusion_{num}.png";
                    glTFMaterial.WithChannelImage( KnownChannel.Occlusion, ComposeTexture( texturePath, tex ) );
                    break;

                case TextureUsage.SamplerReflection:
                    texturePath = $"{name}_emissive_{num}.png";
                    var img = ComposeTexture( texturePath, tex );
                    glTFMaterial.WithChannelImage( KnownChannel.Emissive, img );
                    glTFMaterial.WithEmissive( img, new Vector3( 255, 255, 255 ) );
                    break;

                case TextureUsage.SamplerMask:
                    texturePath = $"{name}_mask_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    // ...what do I do with this?
                    break;

                default:
                    PluginLog.Warning( "Fucked shit, got unhandled TextureUsage " + xivTexture.Key );
                    break;
            }

            num++;
        }

        // I hate you
        glTFMaterial.WithMetallicRoughness( 0 );
    }

    /// <summary>Builds a skeleton tree from a list of .sklb paths.</summary>
    /// <param name="skeletons">A list of HavokXml instances.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    private Dictionary< string, NodeBuilder > GetBoneMap( HavokXml[] skeletons, out NodeBuilder? root ) {
        Dictionary< string, NodeBuilder > boneMap = new();
        root = null;

        foreach( var xml in skeletons ) {
            var skeleton      = xml.GetMainSkeleton();
            var boneNames     = skeleton.BoneNames;
            var refPose       = skeleton.ReferencePose;
            var parentIndices = skeleton.ParentIndices;

            for( var j = 0; j < boneNames.Length; j++ ) {
                var name = boneNames[ j ];
                if( boneMap.ContainsKey( name ) ) continue;

                var bone = new NodeBuilder( name );
                bone.SetLocalTransform( XmlUtils.CreateAffineTransform( refPose[ j ] ), false );

                var boneRootId = parentIndices[ j ];
                if( boneRootId != -1 ) {
                    var parent = boneMap[ boneNames[ boneRootId ] ];
                    parent.AddNode( bone );
                } else { root = bone; }

                boneMap[ name ] = bone;
            }
        }

        return boneMap;
    }

    /// <summary>Exports a model(s) to glTF.</summary>
    /// <param name="outputDir">A directory to write files and textures to.</param>
    /// <param name="models">A list of .mdl paths.</param>
    /// <param name="skeletons">A list of HavokXml instances, obtained from .sklb paths. Care must be taken to provide skeletons in the correct order, or bone map resolving may fail.</param>
    /// <param name="deform">A race code to deform the mesh to, for full body exports.</param>
    public void ExportModel( string outputDir, string[] models, HavokXml[] skeletons, ushort? deform = null, ExportModelType exportType = ExportModelType.DEFAULT ) {
        // TT-exported mdls have incorrect SubmeshBoneMaps (or at least, incompatible with standard Lumina)
        // TODO: Try to correct the submeshbonemap to enable exporting modded models?
        var boneMap      = GetBoneMap( skeletons, out var root );
        var joints       = boneMap.Values.ToArray();
        var raceDeformer = new RaceDeformer( _pbd, boneMap );
        var glTFScene    = new SceneBuilder( models.Length > 0 ? models[ 0 ] : "scene" );
        if( root != null ) glTFScene.AddNode( root );

        foreach( var path in models ) {
            var resolvedMdlPath = ResolvePath( path, exportType );
            var xivModel        = _lumina.GetModel( resolvedMdlPath );
            //File.WriteAllText(Path.Combine(outputDir, Path.GetFileNameWithoutExtension( path ) + ".mdl" ), JsonSerializer.Serialize( xivModel.File));
            var name     = Path.GetFileNameWithoutExtension( path );
            var raceCode = raceDeformer.RaceCodeFromPath( path );

            foreach( var xivMesh in xivModel.Meshes.Where( m => m.Types.Contains( Mesh.MeshType.Main ) ) ) {
                xivMesh.Material.Update( _lumina.GameData );
                var mtrlPath         = xivMesh.Material.ResolvedPath ?? xivMesh.Material.MaterialPath;
                var resolvedMtrlPath = ResolvePath( mtrlPath );
                var xivMaterial      = _lumina.GetMaterial( resolvedMtrlPath, xivMesh.Material.MaterialPath );
                var glTFMaterial = new MaterialBuilder {
                    Name = xivMesh.Material.MaterialPath
                };
                try { ComposeTextures( glTFMaterial, xivMaterial, outputDir ); } catch( Exception e ) {
                    PluginLog.Error(e, "Error composing textures");
                }

                var boneSet       = xivMesh.BoneTable();
                var boneSetJoints = boneSet?.Select( n => boneMap[ n ] ).ToArray();
                var useSkinning   = boneSet != null;

                // Mapping between ID referenced in the mesh and in Havok
                Dictionary< int, int > jointIDMapping = new();
                for( var i = 0; i < boneSetJoints?.Length; i++ ) {
                    var joint = boneSetJoints[ i ];
                    var idx   = joints.ToList().IndexOf( joint );
                    jointIDMapping[ i ] = idx;
                }
                if( xivMesh.Vertices.Length == 0 ) { continue; }
                // Handle submeshes and the main mesh
                var meshBuilder = new MeshBuilder(
                    xivMesh,
                    useSkinning,
                    jointIDMapping,
                    glTFMaterial,
                    raceDeformer
                );

                // Deform for full bodies
                if( raceCode != null && deform != null ) { meshBuilder.SetupDeformSteps( raceCode.Value, deform.Value ); }

                meshBuilder.BuildVertices();

                //_logger?.Debug( $"{xivMesh.Submeshes.Length}" );
                if( xivMesh.Submeshes.Length > 0 ) {
                    for( var i = 0; i < xivMesh.Submeshes.Length; i++ ) {
                        var xivSubmesh = xivMesh.Submeshes[ i ];
                        var subMesh    = meshBuilder.BuildSubmesh( xivSubmesh );
                        subMesh.Name = $"{name}_{xivMesh.MeshIndex}.{i}";
                        meshBuilder.BuildShapes( xivModel.Shapes.Values.ToArray(), subMesh, ( int )xivSubmesh.IndexOffset,
                                                 ( int )( xivSubmesh.IndexOffset + xivSubmesh.IndexNum ) );
                        if( useSkinning ) { glTFScene.AddSkinnedMesh( subMesh, Matrix4x4.Identity, joints ); } else { glTFScene.AddRigidMesh( subMesh, Matrix4x4.Identity ); }
                    }
                } else {
                    var mesh = meshBuilder.BuildMesh();
                    mesh.Name = $"{name}_{xivMesh.MeshIndex}";
                    meshBuilder.BuildShapes( xivModel.Shapes.Values.ToArray(), mesh, 0, xivMesh.Indices.Length );
                    if( useSkinning ) { glTFScene.AddSkinnedMesh( mesh, Matrix4x4.Identity, joints ); } else { glTFScene.AddRigidMesh( mesh, Matrix4x4.Identity ); }
                }
            }
        }

        var glTFModel = glTFScene.ToGltf2();
        glTFModel.SaveAsWavefront( Path.Combine( outputDir, "mesh.obj" ) );
        glTFModel.SaveGLB( Path.Combine( outputDir, "mesh.glb" ) );
        glTFModel.SaveGLTF( Path.Combine( outputDir, "mesh.gltf" ) );
    }

    private string ResolvePath( string path, ExportModelType type = ExportModelType.DEFAULT ) {
        switch( type ) {
            case ExportModelType.UNMODDED:
                return path;
            case ExportModelType.DEFAULT:
                return _pathResolver?.ResolveDefaultPath( path ) ?? path;
            case ExportModelType.PLAYER:
                return _pathResolver?.ResolvePlayerPath( path ) ?? path;
        }
        return path;
    }

    public byte[] ImportModel( string gltfPath, string origModel ) {
        PluginLog.Debug( $"Importing model" );
        var root = ModelRoot.Load( gltfPath );

        Model? orig = null;
        try { orig = _lumina.GetModel( origModel ); } catch( FileNotFoundException ) {
            PluginLog.Error( $"Could not find original model: \"{origModel}\"" );
            //return Array.Empty<byte>();
        }

        var modelFileBuilder = new MdlFileBuilder( root, orig, _logger );

        var (file, vertexData, indexData) = modelFileBuilder.Build();

        if( file == null ) {
            PluginLog.Debug( "Could not build MdlFile" );
            return Array.Empty< byte >();
        }

        using var stream      = new MemoryStream();
        using var modelWriter = new MdlFileWriter( file, stream );

        modelWriter.WriteAll( vertexData, indexData );
        return stream.ToArray();
    }

    public async Task< byte[] > ImportModelAsync( string gltfPath, string origModel ) {
        throw new Exception();
        /*
        PluginLog.Warning( $"Importing Async. Shapes are incorrect." );
        PluginLog.Debug( $"Importing model" );
        var root = ModelRoot.Load( gltfPath );

        Model? orig = null;
        try {
            orig = _lumina.GetModel( origModel );
        }
        catch( FileNotFoundException ) {
            PluginLog.Error( $"Could not find original model: \"{origModel}\"" );
            //return Array.Empty<byte>();
        }

        var modelFileBuilder = new MdlFileBuilder( root, orig, _logger );
        //var (file, vertexData, indexData) = await modelFileBuilder.BuildAsync();

        if( file == null ) {
            PluginLog.Debug( "Could not build MdlFile" );
            return Array.Empty<byte>();
        }

        using var stream = new MemoryStream();
        using var modelWriter = new MdlFileWriter( file, stream );

        modelWriter.WriteAll( vertexData, indexData );
        return stream.ToArray();
        */
    }
}