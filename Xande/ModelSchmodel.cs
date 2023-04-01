using System.Drawing;
using System.IO.Compression;
using System.Numerics;
using Dalamud.Logging;
using Lumina;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using Xande.Havok;
using Mesh = Lumina.Models.Models.Mesh;

namespace Xande;

public class ModelSchmodel {
    private readonly LuminaManager _lumina;

    public ModelSchmodel( LuminaManager lumina ) => _lumina = lumina;
    public ModelSchmodel( GameData gameData ) => _lumina = new LuminaManager( gameData );


    public string THE_PATH = string.Empty;

    private string[] PerchPaths( string path, string extension ) {
        var paths = new List< string >();

        using var fileStream   = File.OpenRead( path );
        using var gzipStream   = new GZipStream( fileStream, CompressionMode.Decompress );
        using var streamReader = new StreamReader( gzipStream );

        while( !streamReader.EndOfStream ) {
            var s = streamReader.ReadLine();
            if( s.EndsWith( extension ) ) paths.Add( s );
        }

        return paths.ToArray();
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

                        var diffuseBlendColour = ColorUtility.Blend(
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 0 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 1 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 2 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 0 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 1 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 2 ] ),
                            normalPixel.B,
                            colorSetBlend
                        );

                        var specularBlendColour = ColorUtility.Blend(
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 4 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 5 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 6 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 4 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 5 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 6 ] ),
                            255,
                            colorSetBlend
                        );

                        var emissionBlendColour = ColorUtility.Blend(
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 8 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 9 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex1 + 10 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 8 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 9 ] ),
                            ColorUtility.HalfToByte( xivMaterial.File.ColorSetInfo.Data[ colorSetIndex2 + 10 ] ),
                            255,
                            colorSetBlend
                        );

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
                            ( int )Math.Round( specularPixel.R * Math.Pow( maskPixel.G / 255.0, 2 ) ),
                            ( int )Math.Round( specularPixel.G * Math.Pow( maskPixel.G / 255.0, 2 ) ),
                            ( int )Math.Round( specularPixel.B * Math.Pow( maskPixel.G / 255.0, 2 ) )
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
                    xivTexture.Value.Save( THE_PATH + texturePath );
                    glTFMaterial.WithChannelImage( KnownChannel.BaseColor, THE_PATH + texturePath );
                    break;
                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    texturePath = $"normal_{num}.png";
                    xivTexture.Value.Save( THE_PATH + texturePath );
                    glTFMaterial.WithChannelImage( KnownChannel.Normal, THE_PATH + texturePath );
                    break;
                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    texturePath = $"specular_{num}.png";
                    xivTexture.Value.Save( THE_PATH + texturePath );
                    //glTFMaterial.WithChannelImage(KnownChannel.SpecularColor, texturePath);
                    glTFMaterial.WithSpecularColor( THE_PATH + texturePath );
                    break;
                case TextureUsage.SamplerWaveMap:
                    texturePath = $"occlusion_{num}.png";
                    xivTexture.Value.Save( THE_PATH + texturePath );
                    glTFMaterial.WithChannelImage( KnownChannel.Occlusion, THE_PATH + texturePath );
                    break;
                case TextureUsage.SamplerReflection:
                    texturePath = $"emissive_{num}.png";
                    xivTexture.Value.Save( THE_PATH + texturePath );
                    glTFMaterial.WithChannelImage( KnownChannel.Emissive, THE_PATH + texturePath );
                    break;
                default:
                    PluginLog.Log( "Fucked shit, got unhandled TextureUsage " + xivTexture.Key );
                    PluginLog.Log( xivTexture.Value.ToString() ?? string.Empty );
                    break;
            }

            num++;
        }
    }

    public void Main( HavokConverter converter ) {
        var path       = "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl";
        var skellyPath = "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb";
        var xivModel   = _lumina.GetModel( path );

        var skellyData   = _lumina.GameData.GetFile( skellyPath ).Data;
        var skellyStream = new MemoryStream( skellyData );
        var skelly       = SklbFile.FromStream( skellyStream );
        var xmlStr       = converter.HkxToXml( skelly.HkxData );
        var xml          = new HavokXml( xmlStr );

        var boneNames      = xml.GetBoneNames();
        var parentIndicies = xml.GetParentIndicies();
        var refPose        = xml.GetReferencePose();

        NodeBuilder                    boneRoot     = null;
        Dictionary< int, NodeBuilder > boneMap      = new();
        Dictionary< string, int >      nameToIdxMap = new();

        for( var i = 0; i < boneNames.Length; i++ ) {
            var name = boneNames[ i ];

            var bone       = new NodeBuilder( name );
            var refPosData = refPose[ i ];

            // Compared with packfile vs tagfile and xivModdingFramework code
            var translation = new Vector3( refPosData[ 0 ], refPosData[ 1 ], refPosData[ 2 ] );
            var rotation    = new Quaternion( refPosData[ 4 ], refPosData[ 5 ], refPosData[ 6 ], refPosData[ 7 ] );
            var scale       = new Vector3( refPosData[ 8 ], refPosData[ 9 ], refPosData[ 10 ] );

            PluginLog.Verbose( $"idx {i}" );
            PluginLog.Verbose( $"translation: {translation}" );
            PluginLog.Verbose( $"rotation: {rotation}" );
            PluginLog.Verbose( $"scale: {scale}" );

            var affineTransform = new AffineTransform( scale, rotation, translation );
            bone.SetLocalTransform( affineTransform, false );

            var boneRootID = parentIndicies[ i ];
            if( boneRootID == -1 ) { boneRoot = bone; }
            else {
                var parent = boneMap[ boneRootID ];
                parent.AddNode( bone );
            }

            boneMap[ i ]         = bone;
            nameToIdxMap[ name ] = i;

            PluginLog.Verbose( "=====" );
        }

        var glTFScene = new SceneBuilder( path );
        foreach( var xivMesh in xivModel.Meshes ) {
            var boneSet = xivMesh.BoneTable.Select( b => xivModel.StringOffsetToStringMap[ ( int )xivModel.File!.BoneNameOffsets[ b ] ] );
            PluginLog.Verbose( "Bone set: {boneSet}", boneSet );

            var joints           = boneMap.Values.ToArray();
            var incompleteJoints = new List< NodeBuilder >();
            var fakeToRealMap    = new Dictionary< int, int >();
            var seenBones        = new List< string >();

            foreach( var name in boneSet ) {
                if( seenBones.Contains( name ) ) break;

                var boneIdx = nameToIdxMap[ name ];
                var bone    = boneMap[ boneIdx ];
                incompleteJoints.Add( bone );

                var idx = incompleteJoints.Count - 1;
                fakeToRealMap[ idx ] = boneIdx;

                seenBones.Add( name );
            }

            if( !xivMesh.Types.Contains( Mesh.MeshType.Main ) ) continue;

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
                            var boneIndex  = vertex.BlendIndices[ k ];
                            var boneIndex2 = xivMesh.BoneTable[ boneIndex ];
                            var boneIndex3 = fakeToRealMap[ boneIndex2 ];

                            var boneWeight = vertex.BlendWeights != null ? vertex.BlendWeights.Value[ k ] : 0;
                            bindings.Add( ( boneIndex3, boneWeight ) );
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
        //glTFModel.SaveAsWavefront( THE_PATH + "mesh.obj" );
        glTFModel.SaveGLB( THE_PATH + "mesh.glb" );
        glTFModel.SaveGLTF( THE_PATH + "mesh.gltf" );
    }
}