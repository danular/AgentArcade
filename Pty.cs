using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace AgentArcade;

// A program running in a Windows pseudo console (ConPTY), the same mechanism Windows Terminal uses.
// Agent Arcade draws the output itself, so the agents run inside its own window.
sealed class PtySession : IDisposable
{
    public event Action<string>? Output;
    public event Action<int>? Exited;

    readonly IntPtr _console;
    readonly ProcessHandle _process;
    readonly int _processId;
    readonly FileStream _input;
    readonly FileStream _output;
    readonly Thread _reader;
    readonly Channel<byte[]> _writes = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    readonly object _gate = new();
    RegisteredWaitHandle? _exitWait;
    bool _consoleClosed;
    volatile bool _exited;
    int _disposed;

    PtySession(IntPtr console, IntPtr process, int processId, SafeFileHandle input, SafeFileHandle output)
    {
        _console = console;
        _process = new ProcessHandle(process);
        _processId = processId;
        _input = new FileStream(input, FileAccess.Write, 0);
        _output = new FileStream(output, FileAccess.Read, 0);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-read" };
    }

    public static PtySession Start(string commandLine, string directory, IReadOnlyDictionary<string, string?> env, int cols, int rows)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            var error = Marshal.GetLastWin32Error();
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(error);
        }

        var hr = CreatePseudoConsole(Size(cols, rows), inputRead, outputWrite, 0, out var console);
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
        {
            inputWrite.Dispose();
            outputRead.Dispose();
            throw Marshal.GetExceptionForHR(hr) ?? new Win32Exception(hr);
        }

        var attributes = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            environment = EnvironmentBlock(env);
            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            // Empty standard handles make the child use the pseudo console instead of anything
            // this app happened to inherit (for example when it was started from a script).
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            startup.lpAttributeList = attributes;

            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, environment, directory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start " + commandLine);

            CloseHandle(info.hThread);
            return new PtySession(console, info.hProcess, info.dwProcessId, inputWrite, outputRead);
        }
        catch
        {
            outputRead.Dispose();
            inputWrite.Dispose();
            ClosePseudoConsole(console);
            throw;
        }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }

    // Call after subscribing to Output and Exited. Output waits in the pipe until then, so nothing is lost.
    public void Begin()
    {
        _reader.Start();
        _ = Task.Run(WriteLoop);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_process, (_, _) => OnProcessExit(), null, Timeout.Infinite, true);
    }

    public void Write(string text)
    {
        if (_disposed == 0 && text.Length > 0) _writes.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int cols, int rows)
    {
        // Skip instead of waiting when the console is being closed, so the window never stalls.
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (!_consoleClosed) ResizePseudoConsole(_console, Size(cols, rows));
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _writes.Writer.TryComplete();
        // The open process handle keeps this process id from being reused, so this can't hit another program.
        if (!_exited)
        {
            try
            {
                using var process = Process.GetProcessById(_processId);
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
        }
        _ = Task.Run(() =>
        {
            CloseConsole();
            if (_reader.IsAlive) _reader.Join(3000);
            _exitWait?.Unregister(null);
            _input.Dispose();
            _output.Dispose();
            _process.Dispose();
        });
    }

    void OnProcessExit()
    {
        _exited = true;
        // While Dispose kills the process tree, the console must stay open: closing it ends the
        // middle processes early, and their children would then be missed by the tree kill.
        if (Volatile.Read(ref _disposed) == 1) return;
        int code;
        try { code = GetExitCodeProcess(_process.SafeWaitHandle, out var exitCode) ? (int)exitCode : -1; }
        catch (ObjectDisposedException) { code = -1; }
        // Closing the pseudo console ends the output pipe; wait for the last output before reporting the exit.
        CloseConsole();
        if (_reader.IsAlive) _reader.Join(3000);
        Exited?.Invoke(code);
    }

    void CloseConsole()
    {
        lock (_gate)
        {
            if (_consoleClosed) return;
            _consoleClosed = true;
            ClosePseudoConsole(_console);
        }
    }

    void ReadLoop()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16384];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = _output.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, false);
                if (count > 0) Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (IOException)
        {
            // The pipe closes when the console does.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    async Task WriteLoop()
    {
        try
        {
            await foreach (var data in _writes.Reader.ReadAllAsync())
            {
                _input.Write(data, 0, data.Length);
                _input.Flush();
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static IntPtr EnvironmentBlock(IReadOnlyDictionary<string, string?> overrides)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            vars[(string)entry.Key] = entry.Value as string ?? "";
        foreach (var (name, value) in overrides)
        {
            if (value == null) vars.Remove(name);
            else vars[name] = value;
        }
        var block = new StringBuilder();
        foreach (var (name, value) in vars) block.Append(name).Append('=').Append(value).Append('\0');
        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    static COORD Size(int cols, int rows)
        => new() { X = (short)Math.Clamp(cols, 2, 1000), Y = (short)Math.Clamp(rows, 1, 1000) };

    sealed class ProcessHandle : WaitHandle
    {
        public ProcessHandle(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    const int STARTF_USESTDHANDLES = 0x00000100;
    const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll")]
    static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll")]
    static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(SafeWaitHandle hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
