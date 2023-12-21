using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using SkiaSharp;
using Xande.Files;

// ReSharper disable MemberCanBePrivate.Global

namespace Xande;

public class LuminaManager {
    /// <summary>Provided by Lumina.</summary>
    public readonly GameData GameData;

    /// <summary>Used to resolve paths to files. Return a path (either on disk or in SqPack) to override file resolution.</summary>
    public Func< string, string? >? FileResolver;

    /// <summary> Construct a LuminaManager instance. </summary>
    public LuminaManager() {
        var luminaOptions = new LuminaOptions {
            LoadMultithreaded  = true,
            CacheFileResources = true,
#if NEVER // Lumina bug
            PanicOnSheetChecksumMismatch = true,
#else
            PanicOnSheetChecksumMismatch = false,
#endif
            DefaultExcelLanguage = Language.English,
        };

        var processModule = Process.GetCurrentProcess().MainModule;
        if( processModule != null ) { GameData = new GameData( Path.Combine( Path.GetDirectoryName( processModule.FileName )!, "sqpack" ), luminaOptions ); }
        else { throw new Exception( "Could not find process data to create lumina." ); }
    }

    public LuminaManager( Func< string, string? > fileResolver ) : this() => FileResolver = fileResolver;

    public T? GetFile< T >( string path, string? origPath = null ) where T : FileResource {
        var actualPath = FileResolver?.Invoke( path ) ?? path;
        return Path.IsPathRooted( actualPath )
            ? GameData.GetFileFromDisk< T >( actualPath, origPath )
            : GameData.GetFile< T >( actualPath );
    }

    /// <summary>Obtain and parse a model structure from a given path.</summary>
    public Model GetModel( string path ) {
        var mdlFile = GetFile< MdlFile >( path );
        return mdlFile != null
            ? new Model( mdlFile )
            : throw new FileNotFoundException();
    }

    /// <summary>Obtain and parse a material structure.</summary>
    public Material GetMaterial( string path, string? origPath = null) {
        var mtrlFile = GetFile< MtrlFile >( path, origPath );
        return mtrlFile != null
            ? new Material( mtrlFile )
            : throw new FileNotFoundException();
    }

    /// <inheritdoc cref="GetMaterial(string)"/>
    public Material GetMaterial( Material mtrl ) => GetMaterial( mtrl.ResolvedPath ?? mtrl.MaterialPath );

    /// <summary>Obtain and parse a skeleton from a given path.</summary>
    public SklbFile GetSkeleton( string path ) {
        var sklbFile = GetFile< FileResource >( path );
        return sklbFile != null
            ? SklbFile.FromStream( sklbFile.Reader.BaseStream )
            : throw new FileNotFoundException();
    }

    public PbdFile GetPbdFile() {
        return GetFile< PbdFile >( "chara/xls/boneDeformer/human.pbd" )!;
    }

    /// <summary>Obtain and parse a texture to a Bitmap.</summary>
    public unsafe SKBitmap GetTextureBuffer( string path, string? origPath = null ) {
        var texFile = GetFile< TexFile >( path, origPath );
        if( texFile == null ) throw new Exception( $"Lumina was unable to fetch a .tex file from {path}." );
        var texBuffer = texFile.TextureBuffer.Filter( format: TexFile.TextureFormat.B8G8R8A8 );
        var bitmap = new SKBitmap( texBuffer.Width, texBuffer.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul );
        bitmap.Erase(new(0));
        fixed( byte* raw = texBuffer.RawData )
            bitmap.InstallPixels(new SKImageInfo(texBuffer.Width, texBuffer.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul), (nint)raw, texBuffer.Width * 4 );
        return bitmap;
    }

    /// <inheritdoc cref="GetTextureBuffer(string)"/>
    public SKBitmap GetTextureBuffer( Texture texture ) => GetTextureBuffer( texture.TexturePath );

    /// <summary>Save a texture to PNG.</summary>
    /// <param name="basePath">The directory the file should be saved in.</param>
    /// <param name="texture">The texture to be saved.</param>
    /// <returns></returns>
    public string SaveTexture( string basePath, Texture texture ) {
        using var png      = GetTextureBuffer( texture );
        var convPath = texture.TexturePath[ ( texture.TexturePath.LastIndexOf( '/' ) + 1 ).. ] + ".png";

        using var pngData = png.Encode( SKEncodedImageFormat.Png, 100 );
        using var pngStream = pngData.AsStream();
        using var fileStream = File.Create( basePath + convPath );
        pngStream.CopyTo( fileStream );
        return convPath;
    }
}