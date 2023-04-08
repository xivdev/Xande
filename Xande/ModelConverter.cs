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
using Mesh = Lumina.Models.Models.Mesh;

// ReSharper disable InconsistentNaming

namespace Xande;

public static class ModelExtensions {
    public static string[]? BoneTable( this Mesh mesh ) {
        var rawMesh = mesh.Parent.File!.Meshes[ mesh.MeshIndex ];
        if( rawMesh.BoneTableIndex == 255 ) { return null; }

        var rawTable = mesh.Parent.File!.BoneTables[ rawMesh.BoneTableIndex ];
        return rawTable.BoneIndex.Take( rawTable.BoneCount ).Select( b => mesh.Parent.StringOffsetToStringMap[ ( int )mesh.Parent.File!.BoneNameOffsets[ b ] ] ).ToArray();
    }

    public static Vertex VertexByIndex( this Mesh mesh, int index ) => mesh.Vertices[ mesh.Indices[ index ] ];
}

public class ModelConverter {
    private readonly LuminaManager  _lumina;
    private readonly HavokConverter _converter;
    private readonly PbdFile        _pbd;

    public ModelConverter( LuminaManager lumina, HavokConverter converter ) {
        _lumina    = lumina;
        _converter = converter;
        _pbd       = lumina.GetPbdFile();
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
            string texturePath;
            switch( xivTexture.Key ) {
                case TextureUsage.SamplerColorMap0:
                case TextureUsage.SamplerDiffuse:
                    texturePath = $"diffuse_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.BaseColor, Path.Combine( outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    texturePath = $"normal_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Normal, Path.Combine( outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    texturePath = $"specular_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    //glTFMaterial.WithChannelImage( KnownChannel.SpecularColor, Path.GetFullPath( Path.Combine( outputDir, texturePath ) ) );
                    glTFMaterial.WithSpecularColor( Path.Combine( outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerWaveMap:
                    texturePath = $"occlusion_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Occlusion, Path.Combine( outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerReflection:
                    texturePath = $"emissive_{num}.png";
                    xivTexture.Value.Save( Path.Combine( outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Emissive, Path.Combine( outputDir, texturePath ) );
                    glTFMaterial.WithEmissive( Path.Combine( outputDir, texturePath ), new Vector3( 255, 255, 255 ) );
                    break;
                case TextureUsage.SamplerMask:
                    texturePath = $"mask_{num}.png";
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

    /// <summary>
    /// Fetches a HavokXml from a .sklb path.
    /// </summary>
    /// <param name="skellyPath">Path to a .sklb.</param>
    /// <returns>A newly created HavokXml instance.</returns>
    private HavokXml GetHavokXml( string skellyPath ) {
        var skelly = _lumina.GetSkeleton( skellyPath );
        var xmlStr = _converter.HkxToXml( skelly.HkxData );
        return new HavokXml( xmlStr );
    }

    /// <summary>
    /// Builds a skeleton tree from a list of .sklb paths.
    /// </summary>
    /// <param name="skellyPaths">A list of .sklb paths.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    private Dictionary< string, NodeBuilder > GetBoneMap( string[] skellyPaths, out NodeBuilder? root ) {
        Dictionary< string, NodeBuilder > boneMap = new();
        root = null;

        foreach( var skellyPath in skellyPaths ) {
            var xml           = GetHavokXml( skellyPath );
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
                }
                else { root = bone; }

                boneMap[ name ] = bone;
            }
        }

        return boneMap;
    }

    /// <summary>
    /// Exports a model(s) to glTF.
    /// </summary>
    /// <param name="outputDir">A directory to write files and textures to.</param>
    /// <param name="models">A list of .mdl paths.</param>
    /// <param name="skeletons">A list of .sklb paths. Care must be taken to provide skeletons in the correct order, or bone map resolving may fail.</param>
    /// <param name="deform">A race code to deform the mesh to, for full body exports.</param>
    public void ExportModel( string outputDir, string[] models, string[] skeletons, ushort? deform = null ) {
        var boneMap      = GetBoneMap( skeletons, out var root );
        var joints       = boneMap.Values.ToArray();
        var raceDeformer = new RaceDeformer( _pbd, boneMap );
        var glTFScene    = new SceneBuilder( models.Length > 0 ? models[ 0 ] : "scene" );
        if( root != null ) glTFScene.AddNode( root );

        foreach( var path in models ) {
            var xivModel       = _lumina.GetModel( path );
            //File.WriteAllText(Path.Combine(outputDir, Path.GetFileNameWithoutExtension( path ) + ".mdl" ), JsonSerializer.Serialize( xivModel.File));
            var name           = Path.GetFileNameWithoutExtension( path );
            var lastMeshOffset = 0;
            var raceCode       = raceDeformer.RaceCodeFromPath( path );

            foreach( var xivMesh in xivModel.Meshes.Where( m => m.Types.Contains( Mesh.MeshType.Main ) ) ) {
                xivMesh.Material.Update( _lumina.GameData );
                var xivMaterial  = _lumina.GetMaterial( xivMesh.Material );
                var glTFMaterial = new MaterialBuilder();

                ComposeTextures( glTFMaterial, xivMaterial, outputDir );

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

                if( xivMesh.Submeshes.Length > 0 ) {
                    // Annoying hack to work around how IndexOffset works in multiple mesh models
                    lastMeshOffset = ( int )xivMesh.Submeshes[ 0 ].IndexOffset;

                    for( var i = 0; i < xivMesh.Submeshes.Length; i++ ) {
                        var xivSubmesh = xivMesh.Submeshes[ i ];
                        var subMesh    = meshBuilder.BuildSubmesh( xivSubmesh, lastMeshOffset );
                        subMesh.Name = $"{name}_{xivMesh.MeshIndex}.{i}";
                        meshBuilder.BuildShapes( xivModel.Shapes.Values.ToArray(), subMesh, (int) xivSubmesh.IndexOffset - lastMeshOffset);
                        if( useSkinning ) { glTFScene.AddSkinnedMesh( subMesh, Matrix4x4.Identity, joints ); }
                        else { glTFScene.AddRigidMesh( subMesh, Matrix4x4.Identity ); }
                    }
                }
                else {
                    var mesh = meshBuilder.BuildMesh( lastMeshOffset );
                    mesh.Name = $"{name}_{xivMesh.MeshIndex}";
                    meshBuilder.BuildShapes( xivModel.Shapes.Values.ToArray(), mesh, lastMeshOffset );
                    if( useSkinning ) { glTFScene.AddSkinnedMesh( mesh, Matrix4x4.Identity, joints ); }
                    else { glTFScene.AddRigidMesh( mesh, Matrix4x4.Identity ); }
                }

            }
        }

        var glTFModel = glTFScene.ToGltf2();
        glTFModel.SaveAsWavefront( Path.Combine( outputDir, "mesh.obj" ) );
        glTFModel.SaveGLB( Path.Combine( outputDir, "mesh.glb" ) );
        glTFModel.SaveGLTF( Path.Combine( outputDir, "mesh.gltf" ) );
    }
}