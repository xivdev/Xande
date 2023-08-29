using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xande.Models.Import {
    internal class MeshVertexData {
        private Dictionary<int, List<byte>> _vertexData = new();
        private Dictionary<int, List<byte>> _shapeVertexData = new();

        public void AddVertexData( Dictionary<int, List<byte>> vertexData ) {
            foreach( var (stream, data) in vertexData ) {
                if( !_vertexData.ContainsKey( stream ) ) {
                    _vertexData[stream] = new List<byte>();
                }
                _vertexData[stream].AddRange( data );
            }
        }

        public void AddShapeVertexData( Dictionary<int, List<byte>> vertexData ) {
            foreach( var (stream, data) in vertexData ) {
                if( !_shapeVertexData.ContainsKey( stream ) ) {
                    _shapeVertexData[stream] = new List<byte>();
                }
                _shapeVertexData[stream].AddRange( data );
            }
        }

        public List<byte> GetBytes() {
            var ret = new List<byte>();
            foreach( var stream in _vertexData.Keys ) {
                ret.AddRange( _vertexData[stream] );

                if( _shapeVertexData.Count > 0 ) {
                    if( !_shapeVertexData.ContainsKey( stream ) ) {
                        PluginLog.Error( $"Vertices and shape vertices do not have the same stream: {stream}" );
                        continue;
                    }
                    ret.AddRange( _shapeVertexData[stream] );
                }
            }
            return ret;
        }
    }
}
