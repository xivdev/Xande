using System.Runtime.InteropServices;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.Havok;

// ReSharper disable InconsistentNaming

namespace Xande.Havok;

/// <summary>
/// Responsible for calling Havok functions in the game binary. Allows you to convert between .hkx (binary) and .xml (text).
/// This class functions internally by calling Havok functions in the game binary - while safety is a priority, it is not guaranteed, being unsafe in nature.
/// This class uses disk I/O and temporary files to convert between .hkx and .xml. This can theoretically be worked around with more research around memory streams in Havok.
/// </summary>
public unsafe class HavokConverter {
    private delegate hkResource* hkSerializeUtil_LoadDelegate(
        char* path,
        void* idk,
        hkSerializeUtil_LoadOptions* options
    );

    private delegate hkResult* hkSerializeUtil_SaveDelegate(
        hkResult* result,
        void* obj,
        hkClass* klass,
        hkStreamWriter* writer,
        hkFlags< hkSerializeUtil_SaveOptionBits, uint > flags
    );

    private delegate void hkOstream_CtorDelegate( hkOStream* self, byte* streamWriter );

    private delegate void hkOstream_DtorDelegate( hkOStream* self );

    [Signature(
        "40 53 48 83 EC 60 41 0F 10 00 48 8B DA 48 8B D1 F2 41 0F 10 48 ?? 48 8D 4C 24 ?? 0F 29 44 24 ?? F2 0F 11 4C 24 ?? E8 ?? ?? ?? ?? 4C 8D 44 24 ?? 48 8B D3 48 8B 48 10 E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 48 8B D8 E8 ?? ?? ?? ?? 48 8B C3 48 83 C4 60 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC 48 89 5C 24 ??" )]
    private readonly hkSerializeUtil_LoadDelegate hkSerializeUtil_Load = null!;

    [Signature( "40 53 48 83 EC 30 8B 44 24 60 48 8B D9 89 44 24 28" )]
    private readonly hkSerializeUtil_SaveDelegate hkSerializeUtil_Save = null!;

    [Signature(
        "48 89 5C 24 ?? 57 48 83 EC 20 C7 41 ?? ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 01 48 8B F9 48 C7 41 ?? ?? ?? ?? ?? 4C 8B C2 48 8B 0D ?? ?? ?? ?? 48 8D 54 24 ?? 41 B9 ?? ?? ?? ?? 48 8B 01 FF 50 28",
        ScanType = ScanType.Text )]
    private readonly hkOstream_CtorDelegate hkOstream_Ctor = null!;

    [Signature( "E8 ?? ?? ?? ?? 44 8B 44 24 ?? 4C 8B 7C 24 ??" )]
    private readonly hkOstream_DtorDelegate hkOstream_Dtor = null!;

    [Signature(
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 83 C4 78 C3 CC CC CC 48 83 EC 78 33 C9 C7 44 24 ?? ?? ?? ?? ?? 89 4C 24 60 48 8D 05 ?? ?? ?? ?? 48 89 4C 24 ?? 48 8D 15 ?? ?? ?? ?? 48 89 4C 24 ?? 45 33 C0 C7 44 24 ?? ?? ?? ?? ?? 44 8D 49 18",
        ScanType = ScanType.StaticAddress )]
    private readonly hkClass* hkRootLevelContainerClass = null;

    [Signature( "48 8B 0D ?? ?? ?? ?? 48 8B 01 FF 50 20 4C 8B 0F 48 8B D3 4C 8B C0 48 8B CF 48 8B 5C 24 ?? 48 83 C4 20 5F 49 FF 61 48", ScanType = ScanType.StaticAddress )]
    private readonly hkBuiltinTypeRegistry** hkBuiltinTypeRegistrySingletonPtr = null;

    private readonly hkBuiltinTypeRegistry* hkBuiltinTypeRegistrySingleton;

    /// <exception cref="KeyNotFoundException">Thrown if signatures fail to match. Signatures were last checked on game version 2023.03.24.0000.0000.</exception>
    public HavokConverter( DalamudPluginInterface pi ) {
        pi.Create< Service >()!.GameInteropProvider.InitializeFromAttributes( this );
        hkBuiltinTypeRegistrySingleton = *hkBuiltinTypeRegistrySingletonPtr;
    }

    /// <summary>Creates a temporary file and returns its path.</summary>
    /// <returns>Path to a temporary file.</returns>
    private string CreateTempFile() {
        var s = File.Create( Path.GetTempFileName() );
        s.Close();
        return s.Name;
    }

    /// <summary>Converts a .hkx file to a .xml file.</summary>
    /// <param name="hkx">A byte array representing the .hkx file.</param>
    /// <returns>A string representing the .xml file.</returns>
    /// <exception cref="Exceptions.HavokReadException">Thrown if parsing the .hkx file fails.</exception>
    /// <exception cref="Exceptions.HavokWriteException">Thrown if writing the .xml file fails.</exception>
    public string HkxToXml( byte[] hkx ) {
        var tempHkx = CreateTempFile();
        File.WriteAllBytes( tempHkx, hkx );

        var resource = Read( tempHkx );
        File.Delete( tempHkx );

        if( resource == null ) throw new Exceptions.HavokReadException();

        var options = hkSerializeUtil_SaveOptionBits.SAVE_SERIALIZE_IGNORED_MEMBERS
                      | hkSerializeUtil_SaveOptionBits.SAVE_TEXT_FORMAT
                      | hkSerializeUtil_SaveOptionBits.SAVE_WRITE_ATTRIBUTES;

        var file = Write( resource, options );
        file.Close();

        var bytes = File.ReadAllText( file.Name );
        File.Delete( file.Name );

        return bytes;
    }

    /// <summary>Converts a .xml file to a .hkx file.</summary>
    /// <param name="xml">A string representing the .xml file.</param>
    /// <returns>A byte array representing the .hkx file.</returns>
    /// <exception cref="Exceptions.HavokReadException">Thrown if parsing the .xml file fails.</exception>
    /// <exception cref="Exceptions.HavokWriteException">Thrown if writing the .hkx file fails.</exception>
    public byte[] XmlToHkx( string xml ) {
        var tempXml = CreateTempFile();
        File.WriteAllText( tempXml, xml );

        var resource = Read( tempXml );
        File.Delete( tempXml );

        if( resource == null ) throw new Exceptions.HavokReadException();

        var options = hkSerializeUtil_SaveOptionBits.SAVE_SERIALIZE_IGNORED_MEMBERS
                      | hkSerializeUtil_SaveOptionBits.SAVE_WRITE_ATTRIBUTES;

        var file = Write( resource, options );
        file.Close();

        var bytes = File.ReadAllBytes( file.Name );
        File.Delete( file.Name );

        return bytes;
    }

    /// <summary>
    /// Parses a serialized file into an hkResource*.
    /// The type is guessed automatically by Havok.
    /// This pointer might be null - you should check for that.
    /// </summary>
    /// <param name="filePath">Path to a file on the filesystem.</param>
    /// <returns>A (potentially null) pointer to an hkResource.</returns>
    private hkResource* Read( string filePath ) {
        var path = Marshal.StringToHGlobalAnsi( filePath );

        var loadOptions = stackalloc hkSerializeUtil_LoadOptions[1];
        loadOptions->m_typeInfoReg =
            hkBuiltinTypeRegistrySingleton->vtbl->GetTypeInfoRegistry( hkBuiltinTypeRegistrySingleton );
        loadOptions->m_classNameReg =
            hkBuiltinTypeRegistrySingleton->vtbl->GetClassNameRegistry( hkBuiltinTypeRegistrySingleton );
        loadOptions->options         = new hkEnum< hkSerializeUtil_LoadOptionBits, int >();
        loadOptions->options.Storage = ( int )hkSerializeUtil_LoadOptionBits.LOAD_DEFAULT;

        var resource = hkSerializeUtil_Load( ( char* )path, null, loadOptions );
        return resource;
    }

    /// <summary>Serializes an hkResource* to a temporary file.</summary>
    /// <param name="resource">A pointer to the hkResource, opened through Read().</param>
    /// <param name="optionBits">Flags representing how to serialize the file.</param>
    /// <returns>An opened FileStream of a temporary file. You are expected to read the file and delete it.</returns>
    /// <exception cref="Exceptions.HavokWriteException">Thrown if accessing the root level container fails.</exception>
    /// <exception cref="Exceptions.HavokFailureException">Thrown if an unknown failure in writing occurs.</exception>
    private FileStream Write(
        hkResource* resource,
        hkSerializeUtil_SaveOptionBits optionBits
    ) {
        var oStream  = stackalloc hkOStream[1];
        var tempFile = CreateTempFile();
        var path     = Marshal.StringToHGlobalAnsi( tempFile );
        hkOstream_Ctor( oStream, ( byte* )path );

        var result = stackalloc hkResult[1];
        var options = new hkFlags< hkSerializeUtil_SaveOptionBits, uint > {
            Storage = ( uint )optionBits,
        };

        try {
            var                   name         = @"hkRootLevelContainer"u8;
            var                   resourceVtbl = *( hkResourceVtbl** )resource;
            hkRootLevelContainer* resourcePtr;
            fixed( byte* n = name ) {
                resourcePtr = ( hkRootLevelContainer* )resourceVtbl->getContentsPointer(
                    resource, n, hkBuiltinTypeRegistrySingleton->vtbl->GetTypeInfoRegistry( hkBuiltinTypeRegistrySingleton ) );
            }

            if( resourcePtr == null ) throw new Exceptions.HavokWriteException();

            hkSerializeUtil_Save( result, resourcePtr, hkRootLevelContainerClass, oStream->m_writer.ptr, options );
        } finally { hkOstream_Dtor( oStream ); }

        if( result->Result == hkResult.hkResultEnum.Failure ) throw new Exceptions.HavokFailureException();

        return new FileStream( tempFile, FileMode.Open );
    }
}