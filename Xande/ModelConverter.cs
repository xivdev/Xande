using System.Drawing;
using System.Numerics;
using Dalamud.Logging;
using Lumina;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
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


    private unsafe void ComposeTextures( MaterialBuilder glTFMaterial, Mesh xivMesh, Lumina.Models.Materials.Material xivMaterial ) {
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

            var bone       = new NodeBuilder( name );
            var refPosData = refPose[ i ];

            // Compared with packfile vs tagfile and xivModdingFramework code
            var translation = new Vector3( refPosData[ 0 ], refPosData[ 1 ], refPosData[ 2 ] );
            var rotation    = new Quaternion( refPosData[ 4 ], refPosData[ 5 ], refPosData[ 6 ], refPosData[ 7 ] );
            var scale       = new Vector3( refPosData[ 8 ], refPosData[ 9 ], refPosData[ 10 ] );

            PluginLog.Verbose( $"{i}: {translation} - {rotation} - {scale}" );

            var affineTransform = new AffineTransform( scale, rotation, translation );
            bone.SetLocalTransform( affineTransform, false );

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

    public void Main( string outputDirectory ) {
        _outputDir = outputDirectory;

        var path       = "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl";
        var skellyPath = "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb";
        var xivModel   = _lumina.GetModel( path );

        var skellyData   = _lumina.GameData.GetFile( skellyPath ).Data;
        var skellyStream = new MemoryStream( skellyData );
        var skelly       = SklbFile.FromStream( skellyStream );
        var xmlStr       = _converter.HkxToXml( skelly.HkxData );
        var xml          = new HavokXml( xmlStr );

        var boneMap = GetBoneMap( xml, out var boneRoot );

        var glTFScene = new SceneBuilder( path );

        foreach( var xivMesh in xivModel.Meshes ) {
            if( !xivMesh.Types.Contains( Mesh.MeshType.Main ) ) continue;

            var boneSet       = xivMesh.BoneTable();
            var boneSetJoints = boneSet.Select( n => boneMap[ n ].Item1 ).ToArray();
            var joints        = boneMap.Values.Select( x => x.Item1 ).ToArray();

            var jointIDMapping = new Dictionary< int, int >();
            for( var i = 0; i < boneSetJoints.Length; i++ ) {
                var jointName = boneSetJoints[ i ].Name;
                var jointID   = joints.Select( ( x, j ) => ( x, j ) ).First( x => x.x.Name == jointName ).j;
                jointIDMapping[ i ] = jointID;
            }

            PluginLog.Verbose( "Bone set: {boneSet}", boneSet );
            PluginLog.Verbose( "Joints ({count}): {joints}", joints.Length, joints.Select( x => x.Name ) );

            xivMesh.Material.Update( _lumina.GameData );
            var xivMaterial  = _lumina.GetMaterial( xivMesh.Material );
            var glTFMaterial = new MaterialBuilder();

            ComposeTextures( glTFMaterial, xivMesh, xivMaterial );

            var TvG = VertexUtility.GetVertexGeometryType( xivMesh.Vertices );
            var TvM = VertexUtility.GetVertexMaterialType( xivMesh.Vertices );
            var TvS = VertexUtility.GetVertexSkinningType( xivMesh.Vertices );

            var glTFMesh = ( IMeshBuilder< MaterialBuilder > )Activator.CreateInstance( typeof( MeshBuilder< ,,, > ).MakeGenericType( typeof( MaterialBuilder ), TvG, TvM, TvS ),
                string.Empty );
            var glTFPrimitive = glTFMesh.UsePrimitive( glTFMaterial );
            for( var i = 0; i < xivMesh.Indices.Length; i += 3 ) {
                var triangle = new Vertex[] {
                    xivMesh.Vertices[ xivMesh.Indices[ i + 0 ] ],
                    xivMesh.Vertices[ xivMesh.Indices[ i + 1 ] ],
                    xivMesh.Vertices[ xivMesh.Indices[ i + 2 ] ],
                };

                var vertexBuilderType = typeof( VertexBuilder< ,, > ).MakeGenericType( TvG, TvM, TvS );

                var vertexBuilderParams = new List< object >[3];
                var vertexTvGParams     = new List< object >[3];
                var vertexTvMParams     = new List< object >[3];
                var vertexTvSParams     = new List< object >[3];

                for( var j = 0; j < 3; j++ ) {
                    var vertex    = triangle[ j ];
                    var vbParams  = vertexBuilderParams[ j ] = new List< object >();
                    var TvGParams = vertexTvGParams[ j ] = new List< object >();
                    var TvMParams = vertexTvMParams[ j ] = new List< object >();
                    var TvSParams = vertexTvSParams[ j ] = new List< object >();

                    TvGParams.Add(
                        new Vector3(
                            vertex.Position.Value.X,
                            vertex.Position.Value.Y,
                            vertex.Position.Value.Z
                        )
                    );

                    // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
                    if( TvG != typeof( VertexPosition ) ) {
                        TvGParams.Add(
                            new Vector3(
                                vertex.Normal.Value.X,
                                vertex.Normal.Value.Y,
                                vertex.Normal.Value.Z
                            )
                        );
                    }

                    if( TvG == typeof( VertexPositionNormalTangent ) ) {
                        TvGParams.Add(
                            new Vector4(
                                vertex.Tangent1.Value.X,
                                vertex.Tangent1.Value.Y,
                                vertex.Tangent1.Value.Z,
                                // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
                                vertex.Tangent1.Value.W == 1 ? vertex.Tangent1.Value.W : -1
                            )
                        );
                    }


                    // AKA: Has "TextureN" component
                    if( TvM != typeof( VertexColor1 ) ) {
                        TvMParams.Add(
                            new Vector2(
                                vertex.UV.Value.X,
                                vertex.UV.Value.Y
                            )
                        );
                    }

                    // AKA: Has "Color1" component
                    if( TvM != typeof( VertexTexture1 ) ) {
                        TvMParams.Insert( 0,
                            new Vector4(
                                vertex.Color.Value.X,
                                vertex.Color.Value.Y,
                                vertex.Color.Value.Z,
                                vertex.Color.Value.W
                            )
                        );
                    }

                    if( TvS != typeof( VertexEmpty ) ) {
                        var bindings = new List< (int, float) >();
                        for( var k = 0; k < 4; k++ ) {
                            var boneIndex       = vertex.BlendIndices[ k ];
                            var mappedBoneIndex = jointIDMapping[ boneIndex ];
                            var boneWeight      = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;
                            bindings.Add( ( mappedBoneIndex, boneWeight ) );
                        }

                        foreach( var binding in bindings ) {
                            PluginLog.Verbose( "Binding: {binding}", binding );
                            TvSParams.Add( binding );
                        }
                    }

                    PluginLog.Verbose( "TvSParams: {TvSParams}", TvSParams );

                    var TvGVertex = ( IVertexGeometry )Activator.CreateInstance( TvG, TvGParams.ToArray() );
                    var TvMVertex = ( IVertexMaterial )Activator.CreateInstance( TvM, TvMParams.ToArray() );
                    var TvSVertex = ( IVertexSkinning )Activator.CreateInstance( TvS, TvSParams.ToArray() );

                    vbParams.Add( TvGVertex );
                    vbParams.Add( TvMVertex );
                    vbParams.Add( TvSVertex );
                }

                var vertexBuilderA = ( IVertexBuilder )Activator.CreateInstance( vertexBuilderType, vertexBuilderParams[ 0 ].ToArray() );
                var vertexBuilderB = ( IVertexBuilder )Activator.CreateInstance( vertexBuilderType, vertexBuilderParams[ 1 ].ToArray() );
                var vertexBuilderC = ( IVertexBuilder )Activator.CreateInstance( vertexBuilderType, vertexBuilderParams[ 2 ].ToArray() );

                glTFPrimitive.AddTriangle(
                    vertexBuilderA,
                    vertexBuilderB,
                    vertexBuilderC
                );
            }

            glTFScene.AddSkinnedMesh( glTFMesh, Matrix4x4.Identity, joints );
        }

        glTFScene.AddNode( boneRoot );

        var glTFModel = glTFScene.ToGltf2();
        glTFModel.SaveAsWavefront( Path.Combine( _outputDir, "mesh.obj" ) );
        glTFModel.SaveGLB( Path.Combine( _outputDir, "mesh.glb" ) );
        glTFModel.SaveGLTF( Path.Combine( _outputDir, "mesh.gltf" ) );
    }
}