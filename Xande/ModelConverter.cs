using System.Drawing;
using System.Numerics;
using Dalamud.Logging;
using Lumina;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;
using Xande.Havok;
using Mesh = Lumina.Models.Models.Mesh;

namespace Xande;

public static class ModelExtensions {
    public static string[] BoneTable( this Mesh mesh ) {
        var rawMesh  = mesh.Parent.File!.Meshes[ mesh.MeshIndex ];
        var rawTable = mesh.Parent.File!.BoneTables[ rawMesh.BoneTableIndex ];
        return rawTable.BoneIndex.Take( rawTable.BoneCount ).Select( b => mesh.Parent.StringOffsetToStringMap[ ( int )mesh.Parent.File!.BoneNameOffsets[ b ] ] ).ToArray();
    }

    public static Vertex VertexByIndex( this Mesh mesh, int index ) => mesh.Vertices[ mesh.Indices[ index ] ];
}

public class ModelConverter {
    private readonly LuminaManager _lumina;

    private readonly HavokConverter _converter;

    // TODO: no
    private string _outputDir = string.Empty;

    public ModelConverter( LuminaManager lumina, HavokConverter converter ) {
        _lumina    = lumina;
        _converter = converter;
    }

    public ModelConverter( GameData gameData, HavokConverter converter ) {
        _lumina    = new LuminaManager( gameData );
        _converter = converter;
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


    private void ComposeTextures( MaterialBuilder glTFMaterial, Mesh xivMesh, Lumina.Models.Materials.Material xivMaterial ) {
        var xivTextureMap = new Dictionary< TextureUsage, Bitmap >();

        foreach( var xivTexture in xivMaterial.Textures ) { xivTextureMap.Add( xivTexture.TextureUsageRaw, _lumina.GetTextureBuffer( xivTexture ) ); }


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

                xivTextureMap.Add( TextureUsage.SamplerDiffuse, diffuse );
                xivTextureMap.Add( TextureUsage.SamplerSpecular, specular );
                xivTextureMap.Add( TextureUsage.SamplerReflection, emission );

                //glTFMaterial.WithChannelImage(KnownChannel.SpecularFactor, 1.0);
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
                    xivTexture.Value.Save( Path.Combine( _outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.BaseColor, Path.Combine( _outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    texturePath = $"normal_{num}.png";
                    xivTexture.Value.Save( Path.Combine( _outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Normal, Path.Combine( _outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    texturePath = $"specular_{num}.png";
                    xivTexture.Value.Save( Path.Combine( _outputDir, texturePath ) );
                    //glTFMaterial.WithChannelImage(KnownChannel.SpecularColor, texturePath);
                    glTFMaterial.WithSpecularColor( Path.Combine( _outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerWaveMap:
                    texturePath = $"occlusion_{num}.png";
                    xivTexture.Value.Save( Path.Combine( _outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Occlusion, Path.Combine( _outputDir, texturePath ) );
                    break;
                case TextureUsage.SamplerReflection:
                    texturePath = $"emissive_{num}.png";
                    xivTexture.Value.Save( Path.Combine( _outputDir, texturePath ) );
                    glTFMaterial.WithChannelImage( KnownChannel.Emissive, Path.Combine( _outputDir, texturePath ) );
                    break;
                default:
                    PluginLog.Log( "Fucked shit, got unhandled TextureUsage " + xivTexture.Key );
                    PluginLog.Log( xivTexture.Value.ToString() ?? string.Empty );
                    break;
            }

            num++;
        }
    }

    private Dictionary< string, (NodeBuilder, int) > GetBoneMap( HavokXml xml, out NodeBuilder? root ) {
        var boneNames      = xml.GetBoneNames();
        var refPose        = xml.GetReferencePose();
        var parentIndicies = xml.GetParentIndicies();

        Dictionary< string, (NodeBuilder, int) > boneMap = new();
        root = null;

        for( var i = 0; i < boneNames.Length; i++ ) {
            var name = boneNames[ i ];

            var bone = new NodeBuilder( name );
            bone.SetLocalTransform( CreateAffineTransform( refPose[ i ] ), false );

            var boneRootId = parentIndicies[ i ];
            if( boneRootId == -1 ) { root = bone; }
            else {
                var parent = boneMap[ boneNames[ boneRootId ] ];
                parent.Item1.AddNode( bone );
            }

            boneMap[ name ] = ( bone, i );
        }

        return boneMap;
    }

    private static AffineTransform CreateAffineTransform( ReadOnlySpan< float > refPos ) {
        // Compared with packfile vs tagfile and xivModdingFramework code
        if( refPos.Length < 11 ) throw new Exception( "RefPos does not contain enough values for affine transformation." );
        var translation = new Vector3( refPos[ 0 ], refPos[ 1 ], refPos[ 2 ] );
        var rotation    = new Quaternion( refPos[ 4 ], refPos[ 5 ], refPos[ 6 ], refPos[ 7 ] );
        var scale       = new Vector3( refPos[ 8 ], refPos[ 9 ], refPos[ 10 ] );
        return new AffineTransform( scale, rotation, translation );
    }

    public void ExportModel( string outputDirectory, string path ) {
        _outputDir = outputDirectory;

        // TODO: unhardcode
        var skellyPath = "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb";
        var xivModel   = _lumina.GetModel( path );

        var skellyData   = _lumina.GameData.GetFile( skellyPath ).Data;
        var skellyStream = new MemoryStream( skellyData );
        var skelly       = SklbFile.FromStream( skellyStream );
        var xmlStr       = _converter.HkxToXml( skelly.HkxData );
        var xml          = new HavokXml( xmlStr );

        var boneMap = GetBoneMap( xml, out var boneRoot );

        var glTFScene      = new SceneBuilder( path );
        var lastMeshOffset = 0;
        foreach( var xivMesh in xivModel.Meshes.Where( m => m.Types.Contains( Mesh.MeshType.Main ) ) ) {
            xivMesh.Material.Update( _lumina.GameData );
            var xivMaterial  = _lumina.GetMaterial( xivMesh.Material );
            var glTFMaterial = new MaterialBuilder();

            ComposeTextures( glTFMaterial, xivMesh, xivMaterial );

            var boneSet       = xivMesh.BoneTable();
            var boneSetJoints = boneSet.Select( n => boneMap[ n ].Item1 ).ToArray();

            // TODO: why can't we just use boneSetJoints directly without it shattering
            var joints         = boneMap.Values.Select( x => x.Item1 ).ToArray();
            var jointIDMapping = new Dictionary< int, int >();

            for( var i = 0; i < boneSetJoints.Length; i++ ) {
                var jointName = boneSetJoints[ i ].Name;
                var jointID   = joints.Select( ( x, j ) => ( x, j ) ).First( x => x.x.Name == jointName ).j;
                jointIDMapping[ i ] = jointID;
            }

            PluginLog.Verbose( "Bone set: {boneSet}", boneSet );
            PluginLog.Verbose( "Joint ID mapping: {jointIDMapping}", jointIDMapping );

            var meshBuilder   = new MeshBuilder( xivMesh );
            var setMeshOffset = false;
            foreach( var xivSubmesh in xivMesh.Submeshes ) {
                if( !setMeshOffset ) {
                    lastMeshOffset = ( int )xivSubmesh.IndexOffset;
                    setMeshOffset  = true;
                }

                var subMesh = meshBuilder.BuildSubmesh( jointIDMapping, glTFMaterial, xivSubmesh, lastMeshOffset );
                glTFScene.AddSkinnedMesh( subMesh, Matrix4x4.Identity, joints );
            }
        }

        glTFScene.AddNode( boneRoot );

        var glTFModel = glTFScene.ToGltf2();
        glTFModel.SaveAsWavefront( Path.Combine( _outputDir, "mesh.obj" ) );
        glTFModel.SaveGLB( Path.Combine( _outputDir, "mesh.glb" ) );
        glTFModel.SaveGLTF( Path.Combine( _outputDir, "mesh.gltf" ) );
    }
}