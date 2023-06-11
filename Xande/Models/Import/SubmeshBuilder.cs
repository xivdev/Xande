using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mesh = SharpGLTF.Schema2.Mesh;


namespace Xande.Models.Import {
    internal class SubmeshBuilder {
        public int IndexCount => Indices.Count;
        public List<uint> Indices = new List<uint>();
        public int BoneCount { get; } = 0;
        public List<string> Attributes = new();
        public List<string> Shapes => _shapeBuilders.Keys.ToList();
        //public SubmeshShapesBuilder SubmeshShapeBuilder;
        private Dictionary<string, ShapeBuilder> _shapeBuilders = new();
        private Dictionary<string, List<ShapeBuilder>> _shapeBuilderList = new();
        public Mesh Mesh;
        public string MaterialPath = String.Empty;
        private string _material = String.Empty;

        public Vector4 MinBoundingBox = new( 9999f, 9999f, 9999f, -9999f );
        public Vector4 MaxBoundingBox = new( -9999f, -9999f, -9999f, 9999f );

        // Does not include shape vertices
        private int _vertexCount = 0;

        public Dictionary<int, string> OriginalBoneIndexToStrings = new();
        public List<List<Vector3>> AppliedShapes = new();

        public VertexDataBuilder VertexDataBuilder;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="skeleton"></param>
        /// <param name="indexCount">The number of (3d modeling) indices that have come before this submesh</param>
        public SubmeshBuilder( Mesh mesh, List<string> skeleton, MdlStructs.VertexDeclarationStruct vertexDeclarationStruct ) {
            Mesh = mesh;

            foreach( var primitive in Mesh.Primitives ) {
                VertexDataBuilder = new( primitive, vertexDeclarationStruct );
                Indices.AddRange( primitive.GetIndices() );
                var blendIndices = primitive.GetVertexAccessor( "JOINTS_0" )?.AsVector4Array();
                var positions = primitive.GetVertexAccessor( "POSITION" )?.AsVector3Array();
                var material = primitive.Material?.Name;

                if( String.IsNullOrEmpty( material ) ) {
                    // TODO: Figure out what to do in this case
                    // Have a Model as an argument and take the first material from that?
                    PluginLog.Error( "Submesh had null material name" );
                }
                else {
                    if( String.IsNullOrEmpty( MaterialPath ) ) {
                        _material = material;
                        MaterialPath = AdjustMaterialPath( _material );
                    }
                    else {
                        if( material != _material || material != MaterialPath ) {
                            PluginLog.Error( $"Found more than one material name. Original: \"{MaterialPath}\" vs \"{material}\"" );
                        }
                    }
                }

                if( positions != null ) {
                    _vertexCount += positions.Count;

                    foreach( var pos in positions ) {
                        MinBoundingBox.X = MinBoundingBox.X < pos.X ? MinBoundingBox.X : pos.X;
                        MinBoundingBox.Y = MinBoundingBox.Y < pos.Y ? MinBoundingBox.Y : pos.Y;
                        MinBoundingBox.Z = MinBoundingBox.Z < pos.Z ? MinBoundingBox.Z : pos.Z;

                        MaxBoundingBox.X = MaxBoundingBox.X > pos.X ? MaxBoundingBox.X : pos.X;
                        MaxBoundingBox.Y = MaxBoundingBox.Y > pos.Y ? MaxBoundingBox.Y : pos.Y;
                        MaxBoundingBox.Z = MaxBoundingBox.Z > pos.Z ? MaxBoundingBox.Z : pos.Z;
                    }
                }
                else {
                    PluginLog.Error( "This submesh had no positions." );
                }

                if( blendIndices != null ) {
                    foreach( var blendIndex in blendIndices ) {
                        for( var i = 0; i < 4; i++ ) {
                            var index = ( int )blendIndex[i];
                            if( !OriginalBoneIndexToStrings.ContainsKey( index ) && index < skeleton.Count &&
                                skeleton[index] != "n_root" && skeleton[index] != "n_hara" ) {
                                OriginalBoneIndexToStrings.Add( index, skeleton[index] );
                            }
                        }
                    }
                }

                try {
                    var jsonNode = JsonNode.Parse( mesh.Extras.ToJson() );
                    if( jsonNode != null ) {
                        var names = jsonNode["targetNames"]?.AsArray();
                        if( names != null && names.Any() ) {
                            for( var i = 0; i < names.Count; i++ ) {
                                var n = names[i]?.ToString();
                                if (n == null) { continue; }
                                if( n.StartsWith( "shp_" ) ) {
                                    if( !_shapeBuilders.ContainsKey( n ) ) {
                                        _shapeBuilders[n] = new ShapeBuilder( this, n, primitive, i, vertexDeclarationStruct );

                                        _shapeBuilderList[n] = new();
                                    }
                                    else {
                                        _shapeBuilders[n].Add( primitive, i );
                                    }
                                    _shapeBuilderList[n].Add( new ShapeBuilder( this, n, primitive, i, vertexDeclarationStruct ) );
                                }
                                else if( n.StartsWith( "atr_" ) && !Attributes.Contains( n ) ) {
                                    Attributes.Add( n );
                                }
                                else {
                                    //TODO: "applied shapes" ?
                                    //This currently applies all of them, regardless of the value
                                    /*
                                    var target = primitive.GetMorphTargetAccessors( i );
                                    if (target == null) { continue; }
                                    foreach (var kvp in target) {
                                        PluginLog.Debug( $"{kvp.Key} - {kvp.Value}" );
                                    }
                                    target.TryGetValue( "POSITION", out var shapeAccessor );
                                    var appliedPositions = shapeAccessor?.AsVector3Array();
                                    if ( appliedPositions != null && appliedPositions.Any() && appliedPositions.Where(x => x != Vector3.Zero).Any()) {
                                        AppliedShapes.Add( appliedPositions.ToList());
                                    }
                                    */
                                }
                            }
                        }
                    }
                    else {
                        PluginLog.Debug( "Mesh contained no extras." );
                    }
                }
                catch( Exception ex ) {

                }
            }

            BoneCount = OriginalBoneIndexToStrings.Keys.Count;
        }

        public List<ushort> GetSubmeshBoneMap( List<string> bones ) {
            var ret = new List<ushort>();
            foreach( var val in OriginalBoneIndexToStrings.Values ) {
                var index = bones.IndexOf( val );
                ret.Add( ( ushort )index );
            }
            return ret;
        }

        public int GetVertexCount( bool includeShapes, List<string>? strings = null ) {
            //return _vertexCount + ( includeShapes ? SubmeshShapeBuilder.GetVertexCount( strings ) : 0 );
            var ret = _vertexCount;
            if( includeShapes ) {
                foreach( var shapeName in _shapeBuilders.Keys ) {
                    if( strings == null || strings.Contains( shapeName ) ) {
                        ret += _shapeBuilders[shapeName].GetVertexCount();
                    }
                }
            }
            return ret;
        }

        public uint GetAttributeIndexMask( List<string> attributes ) {
            var ret = 0;
            for( var i = 0; i < attributes.Count; i++ ) {
                if( Attributes.Contains( attributes[i] ) ) {
                    ret += ( 1 << i );
                }
            }
            return ( uint )ret;
        }

        public List<byte> GetIndexData( int indexCounter = 0 ) {
            var ret = new List<byte>();
            foreach( var index in Indices ) {
                ret.AddRange( BitConverter.GetBytes( ( ushort )( index + indexCounter ) ) );
            }
            return ret;
        }

        public static string AdjustMaterialPath( string mat ) {
            var ret = mat;
            // TODO: More nuanced method for adjusting the material path
            // furniture paths are the entire path
            if( !mat.StartsWith( "/" ) ) {
                ret = "/" + ret;
            }
            if( !mat.EndsWith( ".mtrl" ) ) {
                ret = ret + ".mtrl";
            }
            return ret;
        }

        // TODO: Do we actually need to calculate these values?
        public List<Vector4> CalculateBitangents() {
            var tris = Mesh.EvaluateTriangles();
            var ret = new List<Vector4>();

            // https://github.com/TexTools/xivModdingFramework/blob/f8d442688e61851a90646e309b868783c47122be/xivModdingFramework/Models/Helpers/ModelModifiers.cs#L1575
            // I think these are functionally the same? Regardless, this is where it came from
            foreach( var tri in tris ) {
                var vertex1Pos = tri.A.GetGeometry().GetPosition();
                var vertex2Pos = tri.B.GetGeometry().GetPosition();
                var vertex3Pos = tri.C.GetGeometry().GetPosition();

                var vertex1UV = tri.A.GetMaterial().GetTexCoord( 0 );
                var vertex2UV = tri.B.GetMaterial().GetTexCoord( 0 );
                var vertex3UV = tri.C.GetMaterial().GetTexCoord( 0 );

                var deltaX1 = vertex2Pos.X - vertex1Pos.X;
                var deltaX2 = vertex3Pos.X - vertex1Pos.X;
                var deltaY1 = vertex2Pos.Y - vertex1Pos.Y;
                var deltaY2 = vertex3Pos.Y - vertex1Pos.Y;
                var deltaZ1 = vertex2Pos.Z - vertex1Pos.Z;
                var deltaZ2 = vertex3Pos.Z - vertex1Pos.Z;

                var deltaU1 = vertex2UV.X - vertex1UV.X;
                var deltaU2 = vertex3UV.X - vertex1UV.X;
                var deltaV1 = vertex2UV.Y - vertex1UV.Y;
                var deltaV2 = vertex3UV.Y - vertex1UV.Y;

                var r = 1.0f / ( deltaU1 * deltaV2 - deltaU2 * deltaV1 );
                var t = new Vector3( ( deltaV2 * deltaX1 - deltaV1 * deltaX2 ) * r, ( deltaV2 * deltaY1 - deltaV1 * deltaY2 ) * r, ( deltaV2 * deltaZ1 - deltaV1 * deltaZ2 ) * r );
                var b = new Vector3( ( deltaU1 * deltaX2 - deltaU2 * deltaX1 ) * r, ( deltaU1 * deltaY2 - deltaU2 * deltaY1 ) * r, ( deltaU1 * deltaZ2 - deltaU2 * deltaZ1 ) * r );

                tri.A.GetGeometry().TryGetNormal( out var n1 );
                tri.B.GetGeometry().TryGetNormal( out var n2 );
                tri.C.GetGeometry().TryGetNormal( out var n3 );

                var narr = new List<Vector3>() { n1, n2, n3 };
                foreach( var n in narr ) {
                    var tangent = t - ( n * Vector3.Dot( n, t ) );
                    tangent = Vector3.Normalize( tangent );

                    var binormal = Vector3.Cross( n, tangent );
                    binormal = Vector3.Normalize( binormal );

                    var handedness = Vector3.Dot( Vector3.Cross( t, b ), n ) > 0 ? 1 : -1;
                    binormal *= handedness;

                    var val = new Vector4( binormal, handedness );
                    ret.Add( val );
                }
            }
            return ret;
        }

        public Dictionary<int, List<byte>> GetVertexData() {
            return VertexDataBuilder.GetVertexData();
        }

        public Dictionary<string, Dictionary<int, List<byte>>> GetShapeVertexData( List<string>? strings = null ) {
            var ret = new Dictionary<string, Dictionary<int, List<byte>>>();

            foreach (var shapeName in _shapeBuilders.Keys) {
                if (strings == null || strings.Contains(shapeName)) {
                    ret.Add( shapeName, _shapeBuilders[shapeName].GetVertexData() );
                }
            }
            return ret;
        }

        /*
        public Dictionary<string, Dictionary<int, List<byte>>> GetVertexShapeData( MdlStructs.VertexDeclarationStruct vertexDeclarations, List<string>? strings = null, Dictionary<int, int>? blendIndicesDict = null ) {
            var ret = new Dictionary<string, Dictionary<int, List<byte>>>();

            foreach (var shapeName in _shapeBuilders.Keys) {
                if (strings == null || strings.Contains(shapeName)) {
                    ret.Add( shapeName, _shapeBuilders[shapeName].GetVertexData( vertexDeclarations, blendIndicesDict, CalculateBitangents() ) );
                }
            }

            return ret;
        }
        */

        public List<MdlStructs.ShapeValueStruct> GetShapeValues( string str ) {
            if( _shapeBuilders.ContainsKey( str ) ) {
                return _shapeBuilders[str].ShapeValues;
            }
            return new List<MdlStructs.ShapeValueStruct>();
        }
    }
}
