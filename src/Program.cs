using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Program
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageExecuteReadWrite = 0x40;

    private const string TargetProcessName = "CrimsonDesert";
    private const int ExitVirtualKey = 0x77; // F8
    private const int StealVirtualKeyR = 0x52; // R
    private const int StealVirtualKeyG = 0x47; // G
    private const short KeyPressedMask = 0x0001;
    private const int DefaultPollMs = 20;
    private const int DefaultActiveWindowMs = 5000;

    private static volatile bool _running = true;
    private static int _pollMs = DefaultPollMs;
    private static int _activeWindowMs = DefaultActiveWindowMs;
    private static int _durationMs = 0;

    private static readonly HookSpec[] Hooks = new HookSpec[]
    {
        new HookSpec
        {
            Name = "mirror_plus_10",
            Offset = 0x1B37BCE,
            OriginalBytes = new byte[] { 0x48, 0x89, 0x48, 0x10, 0x89, 0x70, 0x0C },
            CaveBytes = BuildHookA(),
            OverwriteLength = 7,
        },
        new HookSpec
        {
            Name = "mirror_plus_48",
            Offset = 0x12ED186,
            OriginalBytes = new byte[] { 0x48, 0x89, 0x43, 0x48, 0x48, 0x8B, 0x46, 0x50 },
            CaveBytes = BuildHookB(),
            OverwriteLength = 8,
        },
    };

    private static int Main(string[] args)
    {
        if (!ParseArgs(args))
        {
            return args.Length > 0 ? 1 : 0;
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        PrintBanner();

        int exitCode = 0;

        try
        {
            Process process = WaitForTargetProcess();
            if (process == null)
            {
                exitCode = 1;
                return exitCode;
            }

            try
            {
                IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead | ProcessVmWrite | ProcessVmOperation, false, unchecked((uint)process.Id));
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine("Could not open CrimsonDesert.exe.");
                    Console.WriteLine(string.Format("Error code: {0}", error));
                    if (error == 5)
                    {
                        Console.WriteLine("Run the starter BAT as administrator.");
                    }

                    exitCode = 1;
                    return exitCode;
                }

                try
                {
                    long moduleBase = process.MainModule.BaseAddress.ToInt64();

                    HookRuntime[] runtimes = new HookRuntime[Hooks.Length];
                    for (int i = 0; i < Hooks.Length; i++)
                    {
                        runtimes[i] = PrepareHook(handle, moduleBase, Hooks[i]);
                    }

                    RunGuardLoop(handle, process.Id, runtimes);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            finally
            {
                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Contribution guard failed:");
            Console.WriteLine(ex.GetType().Name + " " + ex.Message);
            exitCode = 1;
        }

        return exitCode;
    }

    private static void PrintBanner()
    {
        Console.WriteLine("Crimson Desert Contribution Guard");
        Console.WriteLine("Prevents contribution from dropping when stealing.");
        Console.WriteLine("Target process: CrimsonDesert.exe");
        Console.WriteLine(string.Format("Poll interval:  {0} ms", _pollMs));
        Console.WriteLine(string.Format("Active window:  {0} ms after each R/G press", _activeWindowMs));
        Console.WriteLine("Exit hotkey:    F8");
        Console.WriteLine();
    }

    private static bool ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--help" || arg == "-h")
            {
                PrintUsage();
                return false;
            }

            if (arg == "--poll-ms")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --poll-ms");
                    return false;
                }

                int value;
                if (!int.TryParse(args[i + 1], out value) || value < 1)
                {
                    Console.Error.WriteLine("Invalid --poll-ms");
                    return false;
                }

                _pollMs = value;
                i++;
                continue;
            }

            if (arg == "--active-window-ms")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --active-window-ms");
                    return false;
                }

                int value;
                if (!int.TryParse(args[i + 1], out value) || value < 100)
                {
                    Console.Error.WriteLine("Invalid --active-window-ms");
                    return false;
                }

                _activeWindowMs = value;
                i++;
                continue;
            }

            if (arg == "--duration-ms")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --duration-ms");
                    return false;
                }

                int value;
                if (!int.TryParse(args[i + 1], out value) || value < 1)
                {
                    Console.Error.WriteLine("Invalid --duration-ms");
                    return false;
                }

                _durationMs = value;
                i++;
                continue;
            }

            Console.Error.WriteLine("Unknown argument: " + arg);
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  CrimsonDesertContributionInjector.exe [--poll-ms 20] [--active-window-ms 5000] [--duration-ms 5000]");
    }

    private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _running = false;
    }

    private static Process WaitForTargetProcess()
    {
        while (_running)
        {
            Process[] processes = Process.GetProcessesByName(TargetProcessName);
            if (processes.Length > 0)
            {
                Array.Sort(processes, delegate(Process a, Process b) { return a.Id.CompareTo(b.Id); });
                Process result = processes[0];
                for (int i = 1; i < processes.Length; i++)
                {
                    processes[i].Dispose();
                }

                Console.WriteLine("Game found. Guard is ready.");
                return result;
            }

            Thread.Sleep(250);
        }

        return null;
    }

    private static void RunGuardLoop(IntPtr handle, int pid, HookRuntime[] runtimes)
    {
        long activeUntilTicks = 0;
        long stopAtTicks = _durationMs > 0 ? DateTime.UtcNow.Ticks + (TimeSpan.TicksPerMillisecond * _durationMs) : 0;

        Console.WriteLine("Guard idle. Focus the game and steal with R or G.");
        Console.WriteLine("The patch only stays active briefly after each R/G press.");
        Console.WriteLine();

        while (_running)
        {
            if (stopAtTicks != 0 && DateTime.UtcNow.Ticks >= stopAtTicks)
            {
                break;
            }

            if ((GetAsyncKeyState(ExitVirtualKey) & KeyPressedMask) != 0)
            {
                break;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            bool gameFocused = IsProcessFocused(pid);
            bool stealPressed = gameFocused &&
                (((GetAsyncKeyState(StealVirtualKeyR) & KeyPressedMask) != 0) ||
                 ((GetAsyncKeyState(StealVirtualKeyG) & KeyPressedMask) != 0));

            if (stealPressed)
            {
                activeUntilTicks = nowTicks + (TimeSpan.TicksPerMillisecond * _activeWindowMs);
                EnsureHooksActive(handle, runtimes, true);
            }
            else if (AnyHookActive(runtimes) && nowTicks > activeUntilTicks)
            {
                EnsureHooksActive(handle, runtimes, false);
            }

            Thread.Sleep(_pollMs);
        }

        if (AnyHookActive(runtimes))
        {
            EnsureHooksActive(handle, runtimes, false);
        }
    }

    private static HookRuntime PrepareHook(IntPtr handle, long moduleBase, HookSpec spec)
    {
        long targetAddress = moduleBase + spec.Offset;
        byte[] currentBytes = ReadBytes(handle, targetAddress, spec.OriginalBytes.Length);
        if (currentBytes == null)
        {
            throw new InvalidOperationException("Failed to read target bytes for " + spec.Name);
        }

        if (!BytesEqual(currentBytes, spec.OriginalBytes))
        {
            throw new InvalidOperationException(string.Format("Original bytes mismatch for {0} at 0x{1:X}", spec.Name, targetAddress));
        }

        IntPtr cave = AllocateNear(handle, targetAddress, spec.CaveBytes.Length + 16);
        if (cave == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to allocate code cave near 0x" + targetAddress.ToString("X"));
        }

        byte[] caveBytes = new byte[spec.CaveBytes.Length];
        Buffer.BlockCopy(spec.CaveBytes, 0, caveBytes, 0, caveBytes.Length);

        int backJumpOffset = caveBytes.Length - 4;
        int backRelative = checked((int)((targetAddress + spec.OverwriteLength) - (cave.ToInt64() + caveBytes.Length)));
        WriteInt32(caveBytes, backJumpOffset, backRelative);

        if (!WriteBytes(handle, cave.ToInt64(), caveBytes))
        {
            throw new InvalidOperationException("Failed to write code cave for " + spec.Name);
        }

        byte[] patchBytes = new byte[spec.OverwriteLength];
        patchBytes[0] = 0xE9;
        int caveRelative = checked((int)(cave.ToInt64() - (targetAddress + 5)));
        WriteInt32(patchBytes, 1, caveRelative);
        for (int i = 5; i < patchBytes.Length; i++)
        {
            patchBytes[i] = 0x90;
        }

        return new HookRuntime
        {
            Spec = spec,
            TargetAddress = targetAddress,
            CaveAddress = cave.ToInt64(),
            PatchBytes = patchBytes,
            Active = false,
        };
    }

    private static void EnsureHooksActive(IntPtr handle, HookRuntime[] runtimes, bool shouldBeActive)
    {
        for (int i = 0; i < runtimes.Length; i++)
        {
            HookRuntime runtime = runtimes[i];
            if (runtime.Active == shouldBeActive)
            {
                continue;
            }

            byte[] bytes = shouldBeActive ? runtime.PatchBytes : runtime.Spec.OriginalBytes;
            if (!WriteBytes(handle, runtime.TargetAddress, bytes))
            {
                throw new InvalidOperationException(string.Format("Failed to {0} hook {1}", shouldBeActive ? "enable" : "disable", runtime.Spec.Name));
            }

            FlushInstructionCache(handle, new IntPtr(runtime.TargetAddress), new IntPtr(bytes.Length));
            runtime.Active = shouldBeActive;
        }
    }

    private static bool AnyHookActive(HookRuntime[] runtimes)
    {
        for (int i = 0; i < runtimes.Length; i++)
        {
            if (runtimes[i].Active)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProcessFocused(int pid)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        uint foregroundPid;
        GetWindowThreadProcessId(hwnd, out foregroundPid);
        return foregroundPid == pid;
    }

    private static IntPtr AllocateNear(IntPtr handle, long targetAddress, int size)
    {
        const long SearchRange = 0x70000000;
        const long Step = 0x10000;

        for (long distance = 0; distance < SearchRange; distance += Step)
        {
            IntPtr ptr = TryAllocateAt(handle, targetAddress + distance, size);
            if (ptr != IntPtr.Zero && FitsRel32(ptr.ToInt64(), targetAddress))
            {
                return ptr;
            }

            if (distance == 0)
            {
                continue;
            }

            ptr = TryAllocateAt(handle, targetAddress - distance, size);
            if (ptr != IntPtr.Zero && FitsRel32(ptr.ToInt64(), targetAddress))
            {
                return ptr;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr TryAllocateAt(IntPtr handle, long address, int size)
    {
        if (address <= 0)
        {
            return IntPtr.Zero;
        }

        long aligned = address & ~0xFFFFL;
        return VirtualAllocEx(handle, new IntPtr(aligned), new UIntPtr((uint)size), MemCommit | MemReserve, PageExecuteReadWrite);
    }

    private static bool FitsRel32(long cave, long targetAddress)
    {
        long diff = cave - (targetAddress + 5);
        return diff >= int.MinValue && diff <= int.MaxValue;
    }

    private static byte[] ReadBytes(IntPtr handle, long address, int count)
    {
        byte[] buffer = new byte[count];
        IntPtr bytesRead;
        bool ok = ReadProcessMemory(handle, new IntPtr(address), buffer, new IntPtr(count), out bytesRead);
        if (!ok || bytesRead.ToInt32() != count)
        {
            return null;
        }

        return buffer;
    }

    private static bool WriteBytes(IntPtr handle, long address, byte[] data)
    {
        IntPtr bytesWritten;
        bool ok = WriteProcessMemory(handle, new IntPtr(address), data, new IntPtr(data.Length), out bytesWritten);
        return ok && bytesWritten.ToInt32() == data.Length;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] BuildHookA()
    {
        // cmp ecx,[rax+10]
        // jl skip
        // mov [rax+10],rcx
        // skip:
        // mov [rax+0C],esi
        // jmp back
        return new byte[]
        {
            0x3B, 0x48, 0x10,
            0x7C, 0x04,
            0x48, 0x89, 0x48, 0x10,
            0x89, 0x70, 0x0C,
            0xE9, 0x00, 0x00, 0x00, 0x00,
        };
    }

    private static byte[] BuildHookB()
    {
        // cmp eax,[rbx+48]
        // jl skip
        // mov [rbx+48],rax
        // skip:
        // mov rax,[rsi+50]
        // jmp back
        return new byte[]
        {
            0x3B, 0x43, 0x48,
            0x7C, 0x04,
            0x48, 0x89, 0x43, 0x48,
            0x48, 0x8B, 0x46, 0x50,
            0xE9, 0x00, 0x00, 0x00, 0x00,
        };
    }

    private sealed class HookSpec
    {
        public string Name;
        public long Offset;
        public byte[] OriginalBytes;
        public byte[] CaveBytes;
        public int OverwriteLength;
    }

    private sealed class HookRuntime
    {
        public HookSpec Spec;
        public long TargetAddress;
        public long CaveAddress;
        public byte[] PatchBytes;
        public bool Active;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr dwSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
