using System.Runtime.InteropServices;
using FFXIVClientStructs.Havok;

// ReSharper disable EnumUnderlyingTypeIsInt
// ReSharper disable InconsistentNaming

namespace Xande.Havok;

// TODO: move these into ClientStructs
[Flags]
internal enum hkSerializeUtil_SaveOptionBits :
    int {
    SAVE_DEFAULT                   = 0x0,
    SAVE_TEXT_FORMAT               = 0x1,
    SAVE_SERIALIZE_IGNORED_MEMBERS = 0x2,
    SAVE_WRITE_ATTRIBUTES          = 0x4,
    SAVE_CONCISE                   = 0x8,
}

internal enum hkSerializeUtil_LoadOptionBits : int {
    LOAD_DEFAULT            = 0,
    LOAD_FAIL_IF_VERSIONING = 1,
    LOAD_FORCED             = 2,
}

internal struct hkTypeInfoRegistry { }
internal struct hkClassNameRegistry { }

[StructLayout( LayoutKind.Explicit, Size = 0x18 )]
internal unsafe struct hkSerializeUtil_LoadOptions {
    [FieldOffset( 0x0 )]
    internal hkEnum< hkSerializeUtil_LoadOptionBits, int > options;

    [FieldOffset( 0x8 )]
    internal hkClassNameRegistry* m_classNameReg;

    [FieldOffset( 0x10 )]
    internal hkTypeInfoRegistry* m_typeInfoReg;
}

[StructLayout( LayoutKind.Explicit, Size = 0x18 )]
internal struct hkOStream {
    [FieldOffset( 0x10 )]
    internal hkRefPtr< hkStreamWriter > m_writer;
}

[StructLayout( LayoutKind.Explicit, Size = 0x10 )]
internal struct hkStreamWriter { }

[StructLayout( LayoutKind.Explicit, Size = 0x10 )]
internal struct hkStreamReader { }

[StructLayout( LayoutKind.Explicit, Size = 0x50 )]
internal struct hkClass { }

[StructLayout( LayoutKind.Explicit, Size = 0x48 )]
internal unsafe struct hkResourceVtbl {
    [FieldOffset( 0x38 )]
    internal readonly delegate* unmanaged[Stdcall] <void*, byte*, void*, void*> getContentsPointer;
}

[StructLayout( LayoutKind.Explicit, Size = 0x8 )] // probably larger
internal unsafe struct hkBuiltinTypeRegistry {
    [FieldOffset( 0x0 )]
    internal hkBuiltinTypeRegistryVtbl* vtbl;
}

[StructLayout( LayoutKind.Explicit, Size = 0x48 )]
internal unsafe struct hkBuiltinTypeRegistryVtbl {
    [FieldOffset( 0x20 )]
    internal readonly delegate* unmanaged[Stdcall] <hkBuiltinTypeRegistry*, hkTypeInfoRegistry*> GetTypeInfoRegistry;

    [FieldOffset( 0x28 )]
    internal readonly delegate* unmanaged[Stdcall] <hkBuiltinTypeRegistry*, hkClassNameRegistry*> GetClassNameRegistry;
}