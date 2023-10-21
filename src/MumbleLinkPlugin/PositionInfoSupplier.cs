using System;
using System.IO.MemoryMappedFiles;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;

namespace MumbleLinkPlugin
{
    /// <summary>
    /// Supplies position info to mumble Link plugin.
    /// Will cache and repeat latest received position to prevent mumble from losing connection with the game.
    /// </summary>
    public class PositionInfoSupplier: IDisposable
    {
        public int structSize;
        private CancellationTokenSource _supplyCTS;
        private const uint UIVersion = 2;
        private uint UITick;
        private byte[] latestPositionInfo;
        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewStream _stream;
        private BinaryWriter _streamWriter;
        private FileSystemWatcher _watcher;

        public PositionInfoSupplier(int structSize)
        {
            this.structSize = structSize;
            _supplyCTS = new CancellationTokenSource();
        }

        public void Init()
        {
            if (OperatingSystem.IsWindows())
            {
                _mappedFile = MemoryMappedFile.CreateOrOpen("MumbleLink", structSize);
                _stream = _mappedFile.CreateViewStream(0, structSize);
                _streamWriter = new BinaryWriter(_stream, Encoding.Unicode, true);
                return;
            }

            var fileName = $"/dev/shm/MumbleLink.{getuid()}";
            void OnCreated(object sender, FileSystemEventArgs e)
            {
                _mappedFile = MemoryMappedFile.CreateFromFile(fileName);
                _stream = _mappedFile.CreateViewStream(0, structSize);
                _streamWriter = new BinaryWriter(_stream, Encoding.UTF32, true);
            }
            void OnDeleted(object sender, FileSystemEventArgs e)
            {
                _streamWriter.Dispose();
                _stream.Dispose();
                _mappedFile.Dispose();
                _streamWriter = null;
                _stream = null;
                _mappedFile = null;
            }

            if (File.Exists(fileName))
                OnCreated(null, null);

            _watcher = new FileSystemWatcher(Path.GetDirectoryName(fileName), Path.GetFileName(fileName));
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.EnableRaisingEvents = true;
        }

        public async void Start()
        {
            var ct = _supplyCTS.Token;
            while (!ct.IsCancellationRequested)
            {
                if (latestPositionInfo != null && latestPositionInfo.Length > 0)
                {
                    _stream.Position = 0;
                    _streamWriter.Write(UIVersion);
                    _streamWriter.Write(UITick);
                    _streamWriter.Write(latestPositionInfo);
                    UITick++;
                }
                await Task.Delay(20);
            }
        }

        public void OnNewMessage(byte[] message)
        {
            latestPositionInfo = message;
        }

        [DllImport("libc")]
        private static extern uint getuid();

        public void Dispose()
        {
            _supplyCTS.Cancel();
            _supplyCTS.Dispose();
            _streamWriter?.Dispose();
            _watcher?.Dispose();
            _stream?.Dispose();
            _mappedFile?.Dispose();
        }
    }
}
