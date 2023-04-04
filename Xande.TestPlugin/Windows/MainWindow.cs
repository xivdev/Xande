using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Xande.Havok;

namespace Xande.TestPlugin.Windows;

public class MainWindow : Window, IDisposable {
    private readonly FileDialogManager _fileDialogManager;
    private readonly HavokConverter    _converter;
    private readonly ModelConverter    _modelConverter;

    public MainWindow() : base( "Xande.TestPlugin" ) {
        _fileDialogManager = new FileDialogManager();
        _converter         = new HavokConverter();

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2( 375, 350 ),
            MaximumSize = new Vector2( 1000, 500 ),
        };

        _modelConverter = new ModelConverter( Service.DataManager.GameData, _converter );
    }

    public override void Draw() {
        _fileDialogManager.Draw();

        void DoTheThingWithTheModels( string[] models, string[] skeletons ) {
            var tempDir = Path.Combine( Path.GetTempPath(), "Xande.TestPlugin" );
            Directory.CreateDirectory( tempDir );

            var tempPath = Path.Combine( tempDir, $"model-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" );
            Directory.CreateDirectory( tempPath );

            _modelConverter.ExportModel( tempPath, models, skeletons );
        }

        if( ImGui.Button( "model (full body)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/human/c0101/obj/face/f0002/model/c0101f0002_fac.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_glv.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_dwn.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_sho.mdl"
                },
                new[] {
                    "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb",
                    "chara/human/c0101/skeleton/face/f0002/skl_c0101f0002.sklb"
                }
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "model (chair)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "bg/ffxiv/sea_s1/twn/s1ta/bgparts/s1ta_ga_char1.mdl"
                },
                new string[] { }
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "model (grebuloff)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/monster/m0405/obj/body/b0002/model/m0405b0002.mdl"
                },
                new[] {
                    "chara/monster/m0405/skeleton/base/b0001/skl_m0405b0001.sklb"
                }
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "model (miqote face)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/human/c0801/obj/face/f0102/model/c0801f0102_fac.mdl"
                },
                new[] {
                    "chara/human/c0801/skeleton/base/b0001/skl_c0801b0001.sklb",
                    "chara/human/c0801/skeleton/face/f0002/skl_c0801f0002.sklb"
                }
            );
        }

        if( ImGui.Button( "SKLB->HKX" ) ) {
            _fileDialogManager.OpenFileDialog( "Select SKLB file", "FFXIV Skeleton{.sklb}", ( result, path ) => {
                if( !result ) return;

                Service.Framework.RunOnTick( () => {
                    var sklbData   = File.ReadAllBytes( path );
                    var readStream = new MemoryStream( sklbData );
                    var sklb       = SklbFile.FromStream( readStream );

                    var outputName = Path.GetFileNameWithoutExtension( path ) + ".hkx";

                    _fileDialogManager.SaveFileDialog( "Save HKX file", "Havok Packed File{.hkx}", outputName, ".hkx", ( result, path ) => {
                        if( !result ) return;
                        File.WriteAllBytes( path, sklb.HkxData );
                    } );
                } );
            } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "SKLB->XML" ) ) {
            _fileDialogManager.OpenFileDialog( "Select SKLB file", "FFXIV Skeleton{.sklb}", ( result, path ) => {
                if( !result ) return;

                Service.Framework.RunOnTick( () => {
                    var sklbData   = File.ReadAllBytes( path );
                    var readStream = new MemoryStream( sklbData );
                    var sklb       = SklbFile.FromStream( readStream );
                    var xml        = _converter.HkxToXml( sklb.HkxData );

                    var outputName = Path.GetFileNameWithoutExtension( path ) + ".xml";

                    _fileDialogManager.SaveFileDialog( "Save XML file", "XML{.xml}", outputName, ".xml", ( result, path ) => {
                        if( !result ) return;
                        File.WriteAllText( path, xml );
                    } );
                } );
            } );
        }

        if( ImGui.Button( "HKX->XML" ) ) {
            _fileDialogManager.OpenFileDialog( "Select HKX file", "Havok Packed File{.hkx}", ( result, path ) => {
                if( !result ) return;

                Service.Framework.RunOnTick( () => {
                    var hkx = File.ReadAllBytes( path );
                    var xml = _converter.HkxToXml( hkx );

                    var outputName = Path.GetFileNameWithoutExtension( path ) + ".xml";

                    _fileDialogManager.SaveFileDialog( "Save XML file", "XML{.xml}", outputName, ".xml", ( result, path ) => {
                        if( !result ) return;
                        File.WriteAllText( path, xml );
                    } );
                } );
            } );
        }

        if( ImGui.Button( "XML->HKX" ) ) {
            _fileDialogManager.OpenFileDialog( "Select XML file", "XML{.xml}", ( result, path ) => {
                if( !result ) return;

                Service.Framework.RunOnTick( () => {
                    var xml = File.ReadAllText( path );
                    var hkx = _converter.XmlToHkx( xml );

                    var outputName = Path.GetFileNameWithoutExtension( path ) + ".hkx";

                    _fileDialogManager.SaveFileDialog( "Save HKX file", "Havok Packed File{.hkx}", outputName, ".hkx", ( result, path ) => {
                        if( !result ) return;
                        File.WriteAllBytes( path, hkx );
                    } );
                } );
            } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "XML->SKLB" ) ) {
            _fileDialogManager.OpenFileDialog( "Select XML file", "XML{.xml}", ( result, path ) => {
                if( !result ) return;
                _fileDialogManager.OpenFileDialog( "Select original SKLB file", "FFXIV Skeleton{.sklb}", ( result, path2 ) => {
                    if( !result ) return;

                    Service.Framework.RunOnTick( () => {
                        var xml        = File.ReadAllText( path );
                        var hkx        = _converter.XmlToHkx( xml );
                        var sklbData   = File.ReadAllBytes( path2 );
                        var readStream = new MemoryStream( sklbData );
                        var sklb       = SklbFile.FromStream( readStream );

                        sklb.ReplaceHkxData( hkx );

                        var outputName = Path.GetFileNameWithoutExtension( path ) + ".sklb";

                        _fileDialogManager.SaveFileDialog( "Save SKLB file", "FFXIV Skeleton{.sklb}", outputName, ".sklb", ( result, path ) => {
                            if( !result ) return;
                            var ms = new MemoryStream();
                            sklb.Write( ms );
                            File.WriteAllBytes( path, ms.ToArray() );
                        } );
                    } );
                } );
            } );
        }
    }

    public void Dispose() { }
}