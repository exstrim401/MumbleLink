using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

public class SharedMemory : IDisposable
{
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int shm_open(string name, int oflag, int mode);

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int shm_unlink(string name);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, IntPtr length);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr addr, UIntPtr length);

    [DllImport("libc")]
    private static extern int close(int fd);

    // Platform-specific constants - initialized in static constructor
    private static readonly int O_RDWR;
    private static readonly int O_CREAT;
    private static readonly int S_IRUSR;
    private static readonly int S_IWUSR;
    private static readonly int PROT_READ;
    private static readonly int PROT_WRITE;
    private static readonly int MAP_SHARED;

    // Static constructor to initialize platform-specific constants
    static SharedMemory()
    {
        // These values are common
        O_RDWR = 2;
        PROT_READ = 0x1;
        PROT_WRITE = 0x2;
        MAP_SHARED = 0x1;

        // Platform-specific flags
        if (OperatingSystem.IsMacOS())
        {
            // macOS constants
            O_CREAT = 0x200;
            S_IRUSR = 0x400; // 0400 octal
            S_IWUSR = 0x200; // 0200 octal
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux constants (may vary by distribution)
            O_CREAT = 0x40;
            S_IRUSR = 0x100; // 0400 octal
            S_IWUSR = 0x80;  // 0200 octal
        }
        else
        {
            // Fallback/default values - using Linux conventions
            O_CREAT = 0x40;
            S_IRUSR = 0x100;
            S_IWUSR = 0x80;
        }
    }

    private readonly string _name;
    private readonly int _size;
    private readonly int _fd;
    private readonly IntPtr _address;
    private bool _disposed;
    public UnmanagedMemoryStream _stream;

    public SharedMemory(string name, int size, bool create = true)
    {
        _name = name;
        _size = size;

        // Ensure proper name format for the platform
        string formattedName = name;
        if (!name.StartsWith("/"))
            formattedName = "/" + name;

        // Remove any extra slashes or problematic characters
        while (formattedName.Contains("//"))
            formattedName = formattedName.Replace("//", "/");
        
        // First try to open existing shared memory
        _fd = shm_open(formattedName, O_RDWR, S_IRUSR | S_IWUSR);
        
        // If opening failed and create flag is true, try to create it
        if (_fd == -1 && create)
        {
            _fd = shm_open(formattedName, O_RDWR | O_CREAT, S_IRUSR | S_IWUSR);
            
            // If created successfully, set the size
            if (_fd != -1 && ftruncate(_fd, (IntPtr)size) == -1)
            {
                int truncateError = Marshal.GetLastWin32Error();
                close(_fd);
                throw new Exception($"Failed to set size: {truncateError}");
            }
        }
        
        // If still failed, throw exception
        if (_fd == -1)
        {
            int errno = Marshal.GetLastWin32Error();
            throw new Exception($"Failed to open shared memory: {errno} (Name: {formattedName})");
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
            if (_address != IntPtr.Zero && _address != (IntPtr)(-1))
                munmap(_address, (UIntPtr)_size);
            if (_fd != -1)
                close(_fd);

            // Don't unlink the shared memory segment on disposal
            // This allows Mumble to continue accessing it after our app exits

            _disposed = true;
        }
    }
}
