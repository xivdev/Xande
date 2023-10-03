using System.Text.RegularExpressions;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using Xande.Enums;

namespace Xande.Models.Export;

public class SklbResolver {
    // Native

    private delegate ushort PartialIdDelegate( ushort root, int partial, ushort set );

    [Signature( "44 8B C9 83 EA 01" )]
    private readonly PartialIdDelegate ResolvePartialId = null!;

    // SklbResolver

    public SklbResolver( DalamudPluginInterface pi ) {
        pi.Create< Service >()!.GameInteropProvider.InitializeFromAttributes( this );
    }

    // Path resolver

    public string? Resolve( string mdl ) {
        var split = mdl.Split( "/" );

        var dir = split[ 1 ];
        switch( dir ) {
            case "human":
                return ResolveHuman( mdl, split );
            case "equipment":
                return ResolveHuman( mdl, split, true );
            default:
                if( dir != "demihuman" && dir != "monster" && dir != "weapon" ) return null;
                return string.Format(
                    "chara/{0}/{1}{2:D4}/skeleton/base/b0001/skl_{1}{2:D4}b0001.sklb",
                    dir, dir[ 0 ], ushort.Parse( split[ 2 ][ 1.. ] )
                );
        }
    }

    public string[] ResolveAll( string[] mdls )
        => mdls.Select( Resolve ).OfType< string >().ToArray();

    // Handling for human skeletons

    private readonly Regex          EquipRx  = new(@"c([0-9]{4})e([0-9]{4})_([a-z]{0,}).mdl");
    private readonly List< string > Partials = new() { "body", "face", "hair", "met", "top" };

    public string? ResolveHuman( string mdl, string[] split, bool isEquipment = false ) {
        string type;
        ushort root;
        ushort set;

        if( isEquipment ) {
            // For equipment with additional physics.
            var v = EquipRx.Matches( mdl ).First().Groups;
            type = v[ 3 ].Value;
            root = ushort.Parse( v[ 1 ].Value );
            set  = ushort.Parse( v[ 2 ].Value );
        } else {
            // Face and hair.
            type = split[ 4 ];
            root = ushort.Parse( split[ 2 ][ 1.. ] );
            set  = ushort.Parse( split[ 5 ][ 1.. ] );
        }

        // TODO: Figure out the best way to handle this.
        if( type == "body" ) return null;

        var partial = Partials.IndexOf( type );
        var id      = ResolvePartialId( root, partial, set );
        if( id == 0xFFFF ) return null;

        return string.Format(
            "chara/human/c{0:D4}/skeleton/{1}/{2}{3:D4}/skl_c{0:D4}{2}{3:D4}.sklb",
            root, type, type[ 0 ], id
        );
    }

    // Model ID resolver

    public ushort GetHumanId( byte clan, byte sex = 0, byte bodyType = 1 ) {
        if( bodyType is <= 0 or 3 or > 5 )
            bodyType = 1;
        var x = clan <= 1 ? 0 : clan;
        if( x is > 4 and < 11 )
            x += x < 7 ? 4 : -2;
        x += 1 + sex + x % 2;
        return ( ushort )( x * 100 + bodyType );
    }

    public string ResolveHumanBase( Clan clan, Gender sex, BodyType bodyType = BodyType.Normal )
        => GetHumanBasePath( GetHumanId( ( byte )clan, ( byte )sex, ( byte )bodyType ) );

    public string ResolveHumanBase( byte clan, byte sex = 0, byte bodyType = 1 )
        => GetHumanBasePath( GetHumanId( clan, sex, bodyType ) );

    public string GetHumanBasePath( GenderRace modelId )
        => GetHumanBasePath( ( ushort )modelId );

    public string GetHumanBasePath( ushort modelId )
        => string.Format( "chara/human/c{0:D4}/skeleton/base/b0001/skl_c{0:D4}b0001.sklb", modelId );
}