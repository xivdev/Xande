using System.Drawing;
using System.Drawing.Imaging;
using Lumina;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using Lumina.Models.Models;

// ReSharper disable MemberCanBePrivate.Global

namespace Xande;

public class LuminaManager {
    /// <summary> Provided by Lumina. </summary>
    public readonly GameData GameData;

    /// <summary> Construct a LuminaManager instance. </summary>
    public LuminaManager( GameData gameData ) => GameData = gameData;

    /// <summary> Obtain and parse a model structure from a given path. </summary>
    public Model GetModel( string path ) {
        var mdlFile = GameData.GetFile< MdlFile >( path );
        return mdlFile != null
            ? new Model( mdlFile )
            : throw new Exception( $"Lumina was unable to fetch a .mdl file from {path}." );
    }

    /// <summary> Obtain and parse a material structure. </summary>
    public Material GetMaterial( string path ) {
        var mtrlFile = GameData.GetFile< MtrlFile >( path );
        return mtrlFile != null
            ? new Material( mtrlFile )
            : throw new Exception( $"Lumina was unable to fetch a .mtrl file from {path}." );
    }

    /// <inheritdoc cref="GetMaterial(string)"/>
    public Material GetMaterial( Material mtrl ) => GetMaterial( mtrl.ResolvedPath ?? mtrl.MaterialPath );

    /// <summary> Obtain and parse a texture to a Bitmap.  </summary>
    public unsafe Bitmap GetTextureBuffer( string path ) {
        var texFile = GameData.GetFile< TexFile >( path );
        if( texFile == null ) throw new Exception( $"Lumina was unable to fetch a .tex file from {path}." );
        var texBuffer = texFile.TextureBuffer.Filter( format: TexFile.TextureFormat.B8G8R8A8 );
        fixed( byte* raw = texBuffer.RawData ) { return new Bitmap( texBuffer.Width, texBuffer.Height, texBuffer.Width * 4, PixelFormat.Format32bppArgb, ( nint )raw ); }
    }

    /// <inheritdoc cref="GetTextureBuffer(string)"/>
    public Bitmap GetTextureBuffer( Texture texture ) => GetTextureBuffer( texture.TexturePath );

    /// <summary> Save a texture to PNG. </summary>
    /// <param name="basePath">The directory the file should be saved in.</param>
    /// <param name="texture">The texture to be saved.</param>
    /// <returns></returns>
    public string SaveTexture( string basePath, Texture texture ) {
        var png      = GetTextureBuffer( texture );
        var convPath = texture.TexturePath[ ( texture.TexturePath.LastIndexOf( '/' ) + 1 ).. ] + ".png";

        png.Save( basePath + convPath, ImageFormat.Png );
        return convPath;
    }
}