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
var modelConverter = new ModelConverter(DataManager.GameData, havokConverter);

var outputDir = Path.Combine(Path.GetTempPath(), "XandeModelExport");
var mdlPath = "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl";
modelConverter.ExportModel(outputDir, mdlPath);
```

## Safety

Xande tries to do its best to wrap Havok for you, but at its core, it is a library in another game's address space calling random functions.

When contributing, please make sure to check for null pointers and failed `hkResult`s.

HavokConverter is not thread-safe, so you should use it on the Framework thread (see `Framework.RunOnFrameworkThread`).
