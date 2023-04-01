using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Xande.Havok;

namespace Xande.TestPlugin.Windows;

public class MainWindow : Window, IDisposable {
    private FileDialogManager _fileDialogManager;
    private HavokConverter    _converter;
    private ModelSchmodel     _modelSchmodel;

    public MainWindow() : base( "Xande.TestPlugin" ) {
        _fileDialogManager = new FileDialogManager();
        _converter         = new HavokConverter();

        Size          = new Vector2( 375, 350 );
        SizeCondition = ImGuiCond.FirstUseEver;

        _modelSchmodel = new ModelSchmodel();
    }

    public override void Draw() {
        _fileDialogManager.Draw();

        if( ImGui.Button( "model" ) ) {
            var tempDir = Path.Combine( Path.GetTempPath(), "Xande.TestPlugin" );
            Directory.CreateDirectory( tempDir );

            var tempPath = Path.Combine( tempDir, $"model-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" );
            Directory.CreateDirectory( tempPath );

            _modelSchmodel.THE_PATH = tempPath + "/";
            _modelSchmodel.Main( Service.DataManager.GameData, _converter );
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