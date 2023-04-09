# Xande

Xande is a (WIP) C# library meant to be used in Dalamud plugins for interacting with FINAL FANTASY XIV models. It is able to parse Havok files (`.hkx`, `.xml`) using functions in the client, and perform model exports (imports WIP).

Xande was made possible thanks to:

- [perchbird](https://github.com/lmcintyre)
  - [AnimAssist](https://github.com/lmcintyre/AnimAssist), providing information on the `.sklb` file format
  - Contributing model code to [Lumina](https://github.com/NotAdam/Lumina)
  - Contributing Havok information to [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)
- [Wintermute](https://github.com/pmgr)
  - Writing prototype glTF export code
- [aers](https://github.com/aers)
  - Leading [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)
  - Helping investigate Havok information in the client
- [goatcorp](https://github.com/goatcorp)
  - Making [Dalamud](https://github.com/goatcorp/Dalamud)
- Various mod makers in the [Penumbra Discord server](https://discord.gg/kVva7DHV4r) for examining model exports

## Installation & Usage

Right now, Xande must be referenced as a Git submodule:

```shell
git submodule add https://github.com/xivdev/Xande.git
```

Then reference `Xande.csproj` in your plugin.

---

Xande functions by calling functions of the Havok SDK that were bundled with the FFXIV client. Because of this, right now, it is only possible to use in the context of a Dalamud plugin.

To convert a `.sklb` file into an `.xml` file (and vice versa):

```csharp
// Obtain a byte array, either through the filesystem or through Lumina
var sklbData = File.ReadAllBytes("skl_c0101b0001.sklb");
// Parse the .sklb to obtain the .hkx
var readStream = new MemoryStream(sklbData);
var sklb = SklbFile.FromStream(readStream);

// Do the thing
var converter = new HavokConverter();
var xml = converter.HkxToXml(sklb.HkxData);

// Convert the .xml back into a .hkx
var hkx = converter.XmlToHkx(xml);
// Replace the .sklb's .hkx with the new one
sklb.ReplaceHkxData(hkx);

// Write the new .sklb to disk
var writeStream = new MemoryStream();
sklb.Write(writeStream);
File.WriteAllBytes("skl_c0101b0001.sklb", writeStream.ToArray());
```

To export a model:

```csharp
var havokConverter = new HavokConverter();
var luminaManager = new LuminaManager(DataManager.GameData);
var modelConverter = new ModelConverter(luminaManager);

// outputDir can be any directory that exists and is writable, temp paths are used for demonstration
var outputDir = Path.Combine(Path.GetTempPath(), "XandeModelExport");
Directory.CreateDirectory(outputDir);

// This is Grebuloff
var mdlPaths = new string[] { "chara/monster/m0405/obj/body/b0002/model/m0405b0002.mdl" };
var sklbPaths = new string[] { "chara/monster/m0405/skeleton/base/b0001/skl_m0405b0001.sklb" };

var skeletons = sklbPaths.Select(path => {
    var file = luminaManager.GetFile<FileResource>(path);
    var sklb = SklbFile.FromStream(file.Reader.BaseStream);
    var xmlStr = havokConverter.HkxToXml(sklb.HkxData);
    return new HavokXml(xmlStr);
}).ToArray();

modelConverter.ExportModel(outputDir, mdlPaths, skeletons);
```

Multiple models can be supplied to export them into one scene. When exporting a full body character, pass the `deform` parameter representing the race code of the character.

Skeleton paths can be automatically resolved with the `SklbResolver` class. Note that the skeleton array order is important (base skeletons must come first, and skeletons that depend on other skeletons must come after the dependencies).

## Safety

Xande tries to do its best to wrap Havok for you, but at its core, it is a library in another game's address space calling random functions.

When contributing Havok code, please make sure to check for null pointers and failed `hkResult`s.

Havok functions are not thread-safe, so you should use `HavokConverter` on the Framework thread (see `Framework.RunOnFrameworkThread` and `Framework.RunOnTick`). Model exports are thread-safe.
