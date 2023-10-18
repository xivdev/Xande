This short guide will assume the user has the basic knowledge to import an fbx into TexTools. Also, my experience comes solely from Blender.

# Reminder from TexTools
Export your model with mesh names including X.Y where X and Y are numbers  
Meshes with the same X will be put together in the same mesh/group, while each Y is a submesh/part  

For example:  
Mesh 0.0, Mesh 0.1  
Mesh 1.0, Mesh 1.1, Mesh 1.2  

The actual numbers shouldn't matter  

# GLTF Stuff
Unlike TexTools, Xande takes the .gltf or .glb file as input. Blender can export to .glb just like it can export to .fbx

Some quirks of gltf files  
They are automatically triangulated  
By default, vertices are exported with a maximum of four weights, which is the same for ffxiv  
* The weights are first normalized, then the highest four values are taken while the rest are removed

I've ran into issues where Blender was unable to export the file.  
* Try unticking the "Animation" checkbox. It seemed to fix the problem, at least for me.

# Xande Specifics

| Attributes can be added to submeshes by adding it as a ShapeKey, note that you must have a "Basis" and the attribute must begin with "atr_" | ![attribute assignment cropped](https://github.com/adamm789/Xande/assets/114926302/be1f1bfc-202c-4451-a290-6d4bf1a38ce2) |
| - | - |



| Xande will pull the material name from the Material assigned to the mesh in Blender. If you forget to add it, Penumbra offers the handy ability to assign material names in the Advanced Editing window. | ![material assignment cropped](https://github.com/adamm789/Xande/assets/114926302/dd04b8df-98a6-4380-98c8-4fcf0f34fa03) |
| - | - |

**Important note on material paths**  
If the original .mdl (the one that's being replaced) is expceting a relative path, such /mt_c0101e6111_top_a.mtrl and your model doesn't start with that forward slash, YOUR GAME WILL CRASH.  
If the original .mdl is expecting the full path, such as bgcommon/hou/indoor/... and yours begins with the forward slash, YOU GAME WILL CRASH.  
If you immediately crash upon loading the model, check the material path.  

The other major reason for a crash is because there is no armature when one is expected. So make absolutely sure that your glb has the armature included.
