using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ImGuiNET;
using Lumina.Data;
using Xande.Files;
using Xande.Havok;
using Xande.Models;
using Xande.Models.Export;

namespace Xande.TestPlugin.Windows;

public class MainWindow : Window, IDisposable {
    private readonly FileDialogManager _fileDialogManager;
    private readonly HavokConverter    _converter;
    private readonly LuminaManager     _luminaManager;
    private readonly ModelConverter    _modelConverter;
    private readonly SklbResolver      _sklbResolver;

    private const string SklbFilter = "FFXIV Skeleton{.sklb}";
    private const string PbdFilter  = "FFXIV Bone Deformer{.pbd}";
    private const string HkxFilter  = "Havok Packed File{.hkx}";
    private const string XmlFilter  = "Havok XML File{.xml}";
    private const string GltfFilter = "glTF 2.0 File{.gltf,.glb}";

    enum ExportStatus {
        Idle,
        ParsingSkeletons,
        ExportingModel,
        Done,
        Error
    }

    private ExportStatus _exportStatus = ExportStatus.Idle;

    private string          _modelPaths      = "chara/monster/m0405/obj/body/b0002/model/m0405b0002.mdl";
    private string          _skeletonPaths   = string.Empty;
    private string          _gltfPath        = string.Empty;
    private string          _outputMdlPath   = string.Empty;
    private string          _inputMdl        = string.Empty;
    private int             _deform          = 0;
    private ExportModelType _exportModelType = ExportModelType.UNMODDED;

    public MainWindow() : base( "Xande.TestPlugin" ) {
        _fileDialogManager = new FileDialogManager();
        _converter         = new HavokConverter( Service.PluginInterface );
        if( GetQuickAccessFolders( out var folders ) ) {
            foreach( var folder in folders ) {
                _fileDialogManager.CustomSideBarItems.Add(
                    ( folder.Name,
                        folder.Path,
                        FontAwesomeIcon.Folder,
                        -1
                    ) );
            }
        }

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2( 375, 350 ),
            MaximumSize = new Vector2( 1000, 500 ),
        };

        _luminaManager  = new LuminaManager( origPath => Plugin.Configuration.ResolverOverrides.TryGetValue( origPath, out var newPath ) ? newPath : null );
        _modelConverter = new ModelConverter( _luminaManager, new PenumbraIPCPathResolver( Service.PluginInterface ), new DalamudLogger() );
        _sklbResolver   = new SklbResolver( Service.PluginInterface );
        IsOpen          = Plugin.Configuration.AutoOpen;
    }

    public void Dispose() { }

    public override void Draw() {
        _fileDialogManager.Draw();

        ImGui.BeginTabBar( "Xande.TestPlugin" );
        if( ImGui.BeginTabItem( "Main" ) ) {
            DrawStatus();
            DrawMainTab();
            ImGui.EndTabItem();
        }

        if( ImGui.BeginTabItem( "Paths" ) ) {
            DrawStatus();
            DrawPathsTab();
            ImGui.EndTabItem();
        }

        if( ImGui.BeginTabItem( "Import .gltf" ) ) {
            DrawStatus();
            DrawImportTab();
            ImGui.EndTabItem();
        }

        if( ImGui.BeginTabItem( "Export .mdl" ) ) {
            DrawStatus();
            DrawExportTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // Thank you Otter: https://github.com/Ottermandias/OtterGui/blob/main/Functions.cs#L186
    public static bool GetQuickAccessFolders( out List< (string Name, string Path) > folders ) {
        folders = new List< (string Name, string Path) >();
        try {
            var shellAppType = Type.GetTypeFromProgID( "Shell.Application" );
            if( shellAppType == null )
                return false;

            var shell = Activator.CreateInstance( shellAppType );

            var obj = shellAppType.InvokeMember( "NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] {
                "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}",
            } );
            if( obj == null )
                return false;


            foreach( var fi in ( ( dynamic )obj ).Items() ) {
                if( !fi.IsLink && !fi.IsFolder )
                    continue;

                folders.Add( ( fi.Name, fi.Path ) );
            }

            return true;
        } catch { return false; }
    }

    private void DrawImportTab() {
        var cra      = ImGui.GetContentRegionAvail();
        var textSize = cra with { Y = cra.Y / 2 - 20 };

        if( ImGui.Button( "Browse .gltf" ) ) { OpenFileDialog( "Select gltf file", GltfFilter, path => { _gltfPath = path; } ); }
        ImGui.SameLine();
        ImGui.InputText( "gltf file", ref _gltfPath, 1024 );

        ImGui.InputText( "original .mdl (optional)", ref _modelPaths, 1024 );

        if( ImGui.Button( "Convert" ) ) {
            if( !File.Exists( _gltfPath ) ) {
                PluginLog.Error( $"gltf file does not exist: {_gltfPath}" );
                return;
            }
            if( !_luminaManager.GameData.FileExists( _modelPaths ) ) {
                PluginLog.Error( $"Original mdl file does not exist: {_modelPaths}" );
                //return;
            }
            if( _exportStatus != ExportStatus.ExportingModel ) {
                Task.Run( async () => {
                    try {
                        _exportStatus = ExportStatus.ExportingModel;
                        var data = _modelConverter.ImportModel( _gltfPath, _modelPaths );
                        SaveFileDialog( "Save .mdl", "FFXIV Mdl{.mdl}", "model.mdl", ".mdl", path2 => {
                            PluginLog.Debug( $"Writing file to: {path2}" );
                            File.WriteAllBytes( path2, data );
                            Process.Start( "explorer.exe", Path.GetDirectoryName( path2 ) );
                        } );
                    } catch( Exception ex ) { PluginLog.Error( $"Model could not be imported.\n{ex}" ); } finally { _exportStatus = ExportStatus.Idle; }
                } );
            }
        }
    }

    private void DrawExportTab() {
        var cra      = ImGui.GetContentRegionAvail();
        var textSize = cra with { Y = cra.Y / 2 - 20 };

        ImGui.Text( $"Model file" );
        ImGui.InputTextMultiline( ".mdl file", ref _inputMdl, 1024 * 4, textSize );
        ImGui.Text( $".sklb file(s)" );
        ImGui.InputTextMultiline( ".sklb file", ref _skeletonPaths, 1024 * 4, textSize );

        if( ImGui.Button( "Export .gltf" ) ) {
            if( !string.IsNullOrWhiteSpace( _skeletonPaths ) ) {
                Task.Run( async () => {
                    /*
                    var skel = _sklbResolver.Resolve( _inputMdl );
                    if( skel != null && !_skeletonPaths.Contains( skel ) ) {
                        PluginLog.Debug( $"Adding {skel}" );
                        _skeletonPaths += $"\n {skel}";
                    }
                    */
                    var tempDir = await DoTheThingWithTheModels( _inputMdl.Trim().Split( '\n' ), _skeletonPaths.Trim().Split( '\n' ) );
                    Process.Start( "explorer.exe", tempDir );
                } );
            } else {
                Task.Run( async () => {
                    var s = _sklbResolver.Resolve( _inputMdl );

                    PluginLog.Debug( $"got: {s}" );

                    var tempDir = await DoTheThingWithTheModels( _inputMdl.Trim().Split( '\n' ), new string[] { s } );
                    Process.Start( "explorer.exe", tempDir );
                } );
            }
        }
        ImGui.SameLine();
        /*
        Currently, some model parsing will be needed before we can export modded models
        if( ImGui.BeginCombo( "ExportType", $"{_exportModelType}" ) ) {
            if( ImGui.Selectable( $"{ExportModelType.UNMODDED}" ) ) {
                _exportModelType = ExportModelType.UNMODDED;
            }
            if( ImGui.Selectable( $"{ExportModelType.DEFAULT}" ) ) {
                _exportModelType = ExportModelType.DEFAULT;
            }
            if( ImGui.Selectable( $"{ExportModelType.CHARACTER}" ) ) {
                _exportModelType = ExportModelType.CHARACTER;
            }
            ImGui.EndCombo();
        }
        */
    }

    private void DrawStatus() {
        var status = _exportStatus switch {
            ExportStatus.Idle             => "Idle",
            ExportStatus.ParsingSkeletons => "Parsing skeletons",
            ExportStatus.ExportingModel   => "Exporting model",
            ExportStatus.Done             => "Done",
            ExportStatus.Error            => "Error exporting model",
            _                             => ""
        };

        ImGui.Text( $"Export status: {status}" );
        ImGui.Separator();
    }

    private void DrawMainTab() {
        DrawModel();
        ImGui.Separator();

        DrawParseExport();
        ImGui.Separator();

        DrawConvert();
    }

    private Task< string > DoTheThingWithTheModels( string[] models, string[] skeletons, ushort? deform = null, ExportModelType type = ExportModelType.UNMODDED ) {
        var tempDir = Path.Combine( Path.GetTempPath(), "Xande.TestPlugin" );
        Directory.CreateDirectory( tempDir );

        var tempPath = Path.Combine( tempDir, $"model-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" );
        Directory.CreateDirectory( tempPath );

        return Service.Framework.RunOnTick( () => {
            _exportStatus = ExportStatus.ParsingSkeletons;
            var skellies = skeletons.Select( path => {
                var file = _luminaManager.GetFile< FileResource >( path );
                var sklb = SklbFile.FromStream( file.Reader.BaseStream );
                var xml  = _converter.HkxToXml( sklb.HkxData );
                return new HavokXml( xml );
            } ).ToArray();

            return Task.Run( () => {
                _exportStatus = ExportStatus.ExportingModel;

                try {
                    //_modelConverter.ExportModel( tempPath, models, skellies, deform, _exportModelType );
                    _modelConverter.ExportModel( tempPath, models, skellies, deform, type );
                    PluginLog.Information( "Exported model to {0}", tempPath );
                    _exportStatus = ExportStatus.Done;
                } catch( Exception e ) {
                    PluginLog.Error( e, "Failed to export model" );
                    _exportStatus = ExportStatus.Error;
                }

                return tempPath;
            } );
        } );
    }

    private Task< string > DoTheThingWithTheModels( string[] models, string? baseModel = null, ushort? deform = null, ExportModelType type = ExportModelType.UNMODDED ) {
        var skeletons = _sklbResolver.ResolveAll( models );
        if( baseModel != null )
            skeletons = skeletons.Prepend( baseModel ).ToArray();
        return DoTheThingWithTheModels( models, skeletons, deform: deform, type: type );
    }

    private void OpenFileDialog( string title, string filters, Action< string > callback ) {
        _fileDialogManager.OpenFileDialog( title, filters, ( result, path ) => {
            if( !result ) return;
            Service.Framework.RunOnTick( () => { callback( path ); } );
        } );
    }

    private void SaveFileDialog( string title, string filters, string defaultFileName, string defaultExtension, Action< string > callback ) {
        _fileDialogManager.SaveFileDialog( title, filters, defaultFileName, defaultExtension, ( result, path ) => {
            if( !result ) return;
            Service.Framework.RunOnTick( () => { callback( path ); } );
        } );
    }

    private void DrawModel() {
        if( ImGui.Button( "Model (full body)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/human/c0101/obj/face/f0002/model/c0101f0002_fac.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_glv.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_dwn.mdl",
                    "chara/human/c0101/obj/body/b0001/model/c0101b0001_sho.mdl"
                },
                _sklbResolver.ResolveHumanBase( 1 )
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Model (chair)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "bg/ffxiv/sea_s1/twn/s1ta/bgparts/s1ta_ga_char1.mdl"
                },
                new string[] { }
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Model (grebuloff)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/monster/m0405/obj/body/b0002/model/m0405b0002.mdl"
                }
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Model (miqote face)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/human/c1801/obj/face/f0004/model/c1801f0004_fac.mdl"
                },
                _sklbResolver.ResolveHumanBase( 8, 1 )
            );
        }

        if( ImGui.Button( "Model (jules)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/human/c0801/obj/face/f0102/model/c0801f0102_fac.mdl",
                    "chara/human/c0801/obj/hair/h0008/model/c0801h0008_hir.mdl",
                    "chara/human/c0801/obj/tail/t0002/model/c0801t0002_til.mdl",
                    "chara/equipment/e0287/model/c0201e0287_top.mdl",
                    "chara/equipment/e6024/model/c0201e6024_dwn.mdl",
                    "chara/equipment/e6090/model/c0201e6090_sho.mdl",
                    "chara/equipment/e0227/model/c0101e0227_glv.mdl"
                },
                new[] {
                    "chara/human/c0801/skeleton/base/b0001/skl_c0801b0001.sklb",
                    "chara/human/c0801/skeleton/face/f0002/skl_c0801f0002.sklb",
                    "chara/human/c0801/skeleton/hair/h0009/skl_c0801h0009.sklb",
                    "chara/human/c0801/skeleton/hair/h0008/skl_c0801h0008.sklb"
                },
                deform: 801
            );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Model (Xande)" ) ) {
            DoTheThingWithTheModels(
                new[] {
                    "chara/monster/m0127/obj/body/b0001/model/m0127b0001.mdl",
                    "chara/monster/m0127/obj/body/b0001/model/m0127b0001_top.mdl"
                },
                new[] {
                    "chara/monster/m0127/skeleton/base/b0001/skl_m0127b0001.sklb"
                } );
        }

        ImGui.SameLine();
        if( ImGui.Button( "Model (Gloves)" ) ) {
            DoTheThingWithTheModels( new[] { "chara/equipment/e0180/model/c0201e0180_glv.mdl" }, new string[] { "chara/human/c0201/skeleton/base/b0001/skl_c0201b0001.sklb" } );
        }

        ImGui.Separator();

        if( ImGui.Button( "Model export & import test" ) ) {
            Task.Run( async () => {
                var model    = "chara/equipment/e6111/model/c0201e6111_sho.mdl";
                var skellies = new[] { "chara/human/c0801/skeleton/base/b0001/skl_c0801b0001.sklb" };

                var tempDir = await DoTheThingWithTheModels( new[] { model }, skellies );
                var file    = Path.Combine( tempDir, "mesh.glb" );
                PluginLog.Log( "Importing model..." );
                var bytes = _modelConverter.ImportModel( file, model );
                File.WriteAllBytes( Path.Combine( tempDir, "mesh.mdl" ), bytes );
                PluginLog.Log( "Imported rountrip to {Dir}", tempDir );
            } );
        }
    }

    private void DrawParseExport() {
        if( ImGui.Button( "Parse .sklb" ) ) {
            var file = _luminaManager.GameData.GetFile( "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb" )!;
            var sklb = SklbFile.FromStream( file.Reader.BaseStream );

            var headerHex = BitConverter.ToString( sklb.RawHeader ).Replace( "-", " " );
            var hkxHex    = BitConverter.ToString( sklb.HkxData ).Replace( "-", " " );
            PluginLog.Debug( "Header (len {0}): {1}", sklb.RawHeader.Length, headerHex );
            PluginLog.Debug( "HKX data (len {0}): {1}", sklb.HkxData.Length, hkxHex );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Parse .pbd" ) ) {
            var pbd = _luminaManager.GameData.GetFile< PbdFile >( "chara/xls/boneDeformer/human.pbd" )!;

            PluginLog.Debug( "Header count: {0}", pbd.Headers.Length );
            for( var i = 0; i < pbd.Headers.Length; i++ ) {
                var header = pbd.Headers[ i ];
                PluginLog.Debug( "\tHeader {0} - ID: {1}, offset: {2}", i, header.Id, header.Offset );
            }

            PluginLog.Debug( "Deformer count: {0}", pbd.Deformers.Length );
            for( var i = 0; i < pbd.Deformers.Length; i++ ) {
                var (offset, deformer) = pbd.Deformers[ i ];
                PluginLog.Debug( "\tDeformer {0} (offset {1}) - bone count: {2}", i, offset, deformer.BoneCount );
                for( var j = 0; j < deformer.BoneCount; j++ ) {
                    PluginLog.Debug( "\t\tBone {0} - name: {1}, deform matrix: {2}", j, deformer.BoneNames[ j ], deformer.DeformMatrices[ j ] );
                }
            }
        }

        if( ImGui.Button( "Export .sklb" ) ) {
            var file = _luminaManager.GameData.GetFile( "chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb" )!;
            SaveFileDialog( "Save SKLB file", SklbFilter, "skl_c0101b0001.sklb", ".sklb", path => { File.WriteAllBytes( path, file.Data ); } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Export .pbd" ) ) {
            var file = _luminaManager.GameData.GetFile( "chara/xls/boneDeformer/human.pbd" )!;
            SaveFileDialog( "Save PBD file", PbdFilter, "human.pbd", ".pbd", path => { File.WriteAllBytes( path, file.Data ); } );
        }
    }

    private void DrawConvert() {
        if( ImGui.Button( "SKLB->HKX" ) ) {
            OpenFileDialog( "Select SKLB file", SklbFilter, path => {
                var sklbData   = File.ReadAllBytes( path );
                var sklb       = SklbFile.FromStream( new MemoryStream( sklbData ) );
                var outputName = Path.GetFileNameWithoutExtension( path ) + ".hkx";
                SaveFileDialog( "Save HKX file", HkxFilter, outputName, ".hkx", path2 => { File.WriteAllBytes( path2, sklb.HkxData ); } );
            } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "SKLB->XML" ) ) {
            OpenFileDialog( "Select SKLB file", SklbFilter, path => {
                var sklbData   = File.ReadAllBytes( path );
                var readStream = new MemoryStream( sklbData );
                var sklb       = SklbFile.FromStream( readStream );
                var xml        = _converter.HkxToXml( sklb.HkxData );
                var outputName = Path.GetFileNameWithoutExtension( path ) + ".xml";
                SaveFileDialog( "Save XML file", XmlFilter, outputName, ".xml", path2 => { File.WriteAllText( path2, xml ); } );
            } );
        }

        if( ImGui.Button( "HKX->XML" ) ) {
            OpenFileDialog( "Select HKX file", HkxFilter, path => {
                var hkx        = File.ReadAllBytes( path );
                var xml        = _converter.HkxToXml( hkx );
                var outputName = Path.GetFileNameWithoutExtension( path ) + ".xml";
                SaveFileDialog( "Save XML file", XmlFilter, outputName, ".xml", path2 => { File.WriteAllText( path2, xml ); } );
            } );
        }

        if( ImGui.Button( "XML->HKX" ) ) {
            OpenFileDialog( "Select XML file", XmlFilter, path => {
                var xml        = File.ReadAllText( path );
                var hkx        = _converter.XmlToHkx( xml );
                var outputName = Path.GetFileNameWithoutExtension( path ) + ".hkx";
                SaveFileDialog( "Save HKX file", HkxFilter, outputName, ".hkx", path2 => { File.WriteAllBytes( path2, hkx ); } );
            } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "XML->SKLB" ) ) {
            OpenFileDialog( "Select XML file", XmlFilter, path => {
                OpenFileDialog( "Select original SKLB file", SklbFilter, path2 => {
                    var xml        = File.ReadAllText( path );
                    var hkx        = _converter.XmlToHkx( xml );
                    var sklbData   = File.ReadAllBytes( path2 );
                    var readStream = new MemoryStream( sklbData );
                    var sklb       = SklbFile.FromStream( readStream );

                    sklb.ReplaceHkxData( hkx );

                    var outputName = Path.GetFileNameWithoutExtension( path ) + ".sklb";
                    SaveFileDialog( "Save SKLB file", SklbFilter, outputName, ".sklb", path3 => {
                        var ms = new MemoryStream();
                        sklb.Write( ms );
                        File.WriteAllBytes( path3, ms.ToArray() );
                    } );
                } );
            } );
        }

        var autoOpen = Plugin.Configuration.AutoOpen;
        if( ImGui.Checkbox( "Auto Open Window", ref autoOpen ) ) {
            Plugin.Configuration.AutoOpen = autoOpen;
            Plugin.Configuration.Save();
        }
    }

    private void DrawPathsTab() {
        ImGui.TextUnformatted( "Chuck paths into the bottom text boxes below.\n"
                               + "Use the first box for model paths and the second path for skeleton paths.\n"
                               + "When importing a model, put the original path in the models textbox."
        );

        ImGui.InputInt( "Character deformation", ref _deform );

        var names     = Enum.GetNames( typeof( ExportModelType ) );
        var modelType = ( int )_exportModelType;
        if( ImGui.Combo( "Export type", ref modelType, names, names.Length ) ) { _exportModelType = ( ExportModelType )modelType; }

        var cra      = ImGui.GetContentRegionAvail();
        var textSize = cra with { Y = cra.Y / 2 - 20 };

        ImGui.InputTextMultiline( "Model paths", ref _modelPaths, 1024 * 16, textSize );
        ImGui.InputTextMultiline( "Skeleton paths", ref _skeletonPaths, 1024 * 16, textSize );

        if( ImGui.Button( "Export (create glTF)" ) ) {
            Task.Run( async () => {
                var models = _modelPaths.Trim().Split( '\n' )
                    .Where( x => x.Trim() != string.Empty ).ToArray();
                var skeletons = _skeletonPaths.Trim().Split( '\n' )
                    .Where( x => x.Trim() != string.Empty ).ToArray();

                ushort? deform = _deform == 0 ? null : ( ushort )_deform;
                var tempDir = skeletons.Length == 0
                                  ? await DoTheThingWithTheModels( models, deform: deform, type: _exportModelType )
                                  : await DoTheThingWithTheModels( models, skeletons, deform: deform, type: _exportModelType );

                Process.Start( "explorer.exe", tempDir );
            } );
        }

        ImGui.SameLine();

        if( ImGui.Button( "Import (create MDL)" ) ) {
            OpenFileDialog( "Select glTF", GltfFilter, path => {
                var firstModel = _modelPaths.Trim().Split( '\n' )[ 0 ];
                var data       = _modelConverter.ImportModel( path, firstModel );

                var tempDir = Path.Combine( Path.GetTempPath(), "Xande.TestPlugin" );
                Directory.CreateDirectory( tempDir );
                var tempPath = Path.Combine( tempDir, $"model-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" );
                Directory.CreateDirectory( tempPath );
                var tempFile = Path.Combine( tempPath, "model.mdl" );
                File.WriteAllBytes( tempFile, data );

                Process.Start( "explorer.exe", tempPath );
            } );
        }
    }
}