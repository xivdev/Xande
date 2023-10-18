using Lumina;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xande.GltfImporter {
    internal class MeshVertexData {
        private Dictionary<int, List<byte>> _vertexData = new();
        private Dictionary<int, List<byte>> _shapeVertexData = new();

        private List<Task<Dictionary<int, List<byte>>>> _vertexDataTasks = new();
        private List<Task<Dictionary<int, List<byte>>>> _shapeVertexDataTasks = new();

        private ILogger? _logger;

        public MeshVertexData(ILogger? logger = null) {
            _logger = logger;
        }

        public void AddVertexData(Task<Dictionary<int, List<byte>>> task) {
            _vertexDataTasks.Add(task);
        }

        public void AddShapeVertexData( Task<Dictionary<int, List<byte>>> task ) {
            _shapeVertexDataTasks.Add( task );
        }
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

        public async Task<List<byte>> GetBytesAsync() {
            var ret = new List<byte>();
            await Task.WhenAll( _vertexDataTasks );
            await Task.WhenAll(_shapeVertexDataTasks );
            foreach (var v in _vertexDataTasks ) {
                AddVertexData( await v );
            }
            foreach (var s in _shapeVertexDataTasks) {
                AddShapeVertexData( await s );
            }

            return GetBytes();
        }

        public List<byte> GetBytes() {
            var ret = new List<byte>();
            foreach( var stream in _vertexData.Keys ) {
                ret.AddRange( _vertexData[stream] );

                if( _shapeVertexData.Count > 0 ) {
                    if( !_shapeVertexData.ContainsKey( stream ) ) {
                        _logger?.Error( $"Vertices and shape vertices do not have the same stream: {stream}" );
                        continue;
                    }
                    ret.AddRange( _shapeVertexData[stream] );
                }
            }
            return ret;
        }
    }
}
