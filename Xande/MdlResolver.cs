using System.Runtime.InteropServices;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xande.Enums;

namespace Xande;

public class MdlResolver {
    // Native
    // TODO: Get CharacterUtility into ClientStructs

    private delegate nint EqpDataDelegate( nint a1, ushort a2, uint a3, ushort a4 );

    [Signature( "E8 ?? ?? ?? ?? 66 3B 85 ?? ?? ?? ??" )]
    private readonly EqpDataDelegate GetEqpDataFunc = null!;

    [Signature( "48 8B 0D ?? ?? ?? ?? 48 8D 55 80 44 8B C3", ScanType = ScanType.StaticAddress )]
    private readonly unsafe nint* CharaUtilInstance = null!;

    // CharaUtils wrappers

    private unsafe ushort GetEqpData( ushort a2, uint a3, ushort a4 )
        => ( ushort )GetEqpDataFunc( *CharaUtilInstance, a2, a3, a4 );

    // MdlResolver

    public MdlResolver( DalamudPluginInterface pi ) {
        pi.Create< Service >()!.GameInteropProvider.InitializeFromAttributes( this );
    }

    // Main resolve func

    public string? Resolve( GenderRace dataId, ModelSlot slot, ushort id )
        => Resolve( ( ushort )dataId, ( uint )slot, id );

    public string? Resolve( GenderRace dataId, uint slot, ushort id )
        => Resolve( ( ushort )dataId, slot, id );

    public string? Resolve( ushort dataId, ModelSlot slot, ushort id )
        => Resolve( dataId, ( uint )slot, id );

    public string? Resolve( ushort dataId, uint slot, ushort id ) {
        return slot switch {
            < 10 => ResolveEquipPath( dataId, slot, id ),
            10   => ResolveHairPath( dataId, id ),
            11   => ResolveFacePath( dataId, id ),
            12   => ResolveTailEarsPath( dataId, id ),
            _    => throw new Exception( $"{slot} is not a valid slot index." )
        };
    }

    // Resolve for objects

    public unsafe string? ResolveFor( Human* human, uint slot ) {
        var dataStart = ( nint )human + 0x910;
        var offset = slot < 10
                         ? slot * 4
                         : 0x2A + ( slot - 10 ) * 2;

        var id = ( ushort )Marshal.ReadInt16( dataStart + ( nint )offset );
        if( ( ModelSlot )slot == ModelSlot.Face && id < 201 ) {
            switch( ( Clan )human->Customize.Clan ) {
                case Clan.Xaela or Clan.Lost or Clan.Veena:
                case Clan.KeeperOfTheMoon when human->Customize.BodyType == 4:
                    id -= 100;
                    break;
            }
        }

        return Resolve( human->RaceSexId, slot, id );
    }

    public unsafe string[] ResolveAllFor( Human* human )
        => Enumerable.Range( 0, 13 )
            .Select( n => ResolveFor( human, ( uint )n ) )
            .OfType< string >().ToArray();

    // Equipment

    private readonly string[] SlotTypes = { "met", "top", "glv", "dwn", "sho", "ear", "nek", "wrs", "rir", "ril" };

    public string? ResolveEquipPath( GenderRace dataId, EquipSlot slot, ushort setId )
        => ResolveEquipPath( ( ushort )dataId, ( uint )slot, setId );

    public string? ResolveEquipPath( GenderRace dataId, uint slot, ushort setId )
        => ResolveEquipPath( ( ushort )dataId, slot, setId );

    public string? ResolveEquipPath( ushort dataId, uint slot, ushort setId ) {
        switch( slot ) {
            case 0 or > 4 when setId is 0:
                return null;
            case > 9:
                throw new Exception( $"{slot} is not a valid slot index." );
        }

        if( setId == 0 ) setId = 1;

        var type = slot > 4 ? "accessory" : "equipment";
        var c    = GetEqpData( dataId, slot, setId );

        return string.Format(
            "chara/{0}/{1}{2:D4}/model/c{3:D4}{1}{2:D4}_{4}.mdl",
            type, type[ 0 ], setId, c, SlotTypes[ slot ]
        );
    }

    // Hair / Face / Tail / Ears

    public string? ResolveHairPath( GenderRace dataId, ushort hair )
        => ResolveHairPath( ( ushort )dataId, hair );

    public string? ResolveHairPath( ushort dataId, ushort hair ) => hair == 0
                                                                        ? null
                                                                        : string.Format(
                                                                            "chara/human/c{0:D4}/obj/hair/h{1:D4}/model/c{0:D4}h{1:D4}_hir.mdl",
                                                                            dataId, hair
                                                                        );

    public string? ResolveFacePath( GenderRace dataId, ushort hair )
        => ResolveFacePath( ( ushort )dataId, hair );

    public string? ResolveFacePath( ushort dataId, ushort face ) => face == 0
                                                                        ? null
                                                                        : string.Format(
                                                                            "chara/human/c{0:D4}/obj/face/f{1:D4}/model/c{0:D4}f{1:D4}_fac.mdl",
                                                                            dataId, face
                                                                        );

    public string? ResolveTailEarsPath( GenderRace dataId, ushort hair )
        => ResolveTailEarsPath( ( ushort )dataId, hair );

    public string? ResolveTailEarsPath( ushort dataId, ushort id ) {
        var type = dataId is >= 1700 and < 1900 ? "zear" : "tail";
        return id == 0
                   ? null
                   : string.Format(
                       "chara/human/c{0:D4}/obj/{1}/{2}{3:D4}/model/c{0:D4}{2}{3:D4}_{4}.mdl",
                       dataId, type, type[ 0 ], id, type == "zear" ? "zer" : "til"
                   );
    }
}