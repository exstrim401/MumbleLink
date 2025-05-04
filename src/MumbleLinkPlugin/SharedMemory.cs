using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

public class SharedMemory : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int shm_open(string name, int oflag, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int shm_unlink(string name);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, IntPtr length);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr addr, UIntPtr length);

    [DllImport("libc")]
    private static extern int close(int fd);

    private const int O_RDWR = 2;
    private const int O_CREAT = 0x200;
    private const int S_IRUSR = 0x400;
    private const int S_IWUSR = 0x200;
    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;
    private const int MAP_SHARED = 0x1;

    private readonly string _name;
    private readonly int _size;
    private readonly int _fd;
    private readonly IntPtr _address;
    private bool _disposed;
    public UnmanagedMemoryStream _stream;

    public SharedMemory(string name, int size, bool create = false)
    {
        _name = name;
        _size = size;

        int flags = O_RDWR;
        if (create)
            flags |= O_CREAT;

        _fd = shm_open(name, flags, S_IRUSR | S_IWUSR);
        if (_fd == -1)
            throw new Exception($"Failed to open shared memory: {Marshal.GetLastWin32Error()}");

        if (create && ftruncate(_fd, (IntPtr)size) == -1)
        {
            close(_fd);
            throw new Exception($"Failed to set size: {Marshal.GetLastWin32Error()}");
        }

        _address = mmap(IntPtr.Zero, (UIntPtr)size,
                       PROT_READ | PROT_WRITE,
                       MAP_SHARED, _fd, IntPtr.Zero);

        if (_address == (IntPtr)(-1))
        {
            close(_fd);
            throw new Exception($"Failed to map memory: {Marshal.GetLastWin32Error()}");
        }

        unsafe
        {
            _stream = new UnmanagedMemoryStream((byte*)_address, _size, _size, FileAccess.ReadWrite);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _stream?.Dispose();
            munmap(_address, (UIntPtr)_size);
            close(_fd);
            shm_unlink(_name);
            _disposed = true;
        }
    }
}
