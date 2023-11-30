using SharpGLTF.Scenes;
using Xande.Havok;

namespace Xande.Models;

public static class ModelHelpers {
    /// <summary>Builds a skeleton tree from a list of .sklb paths.</summary>
    /// <param name="skeletons">A list of HavokXml instances.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    public static Dictionary<string, NodeBuilder> GetBoneMap( HavokXml[] skeletons, out NodeBuilder? root ) {
        Dictionary<string, NodeBuilder> boneMap = new();
        root = null;

        foreach( var xml in skeletons ) {
            var skeleton = xml.GetMainSkeleton();
            var boneNames = skeleton.BoneNames;
            var refPose = skeleton.ReferencePose;
            var parentIndices = skeleton.ParentIndices;

            for( var j = 0; j < boneNames.Length; j++ ) {
                var name = boneNames[j];
                if( boneMap.ContainsKey( name ) ) continue;

                var bone = new NodeBuilder( name );
                bone.SetLocalTransform( XmlUtils.CreateAffineTransform( refPose[j] ), false );

                var boneRootId = parentIndices[j];
                if( boneRootId != -1 ) {
                    var parent = boneMap[boneNames[boneRootId]];
                    parent.AddNode( bone );
                }
                else { root = bone; }

                boneMap[name] = bone;
            }
        }

        return boneMap;
    }
}
