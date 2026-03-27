#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>

namespace
{
    constexpr wchar_t kTargetExe[] = L"CrimsonDesert.exe";
    constexpr int kExitVirtualKey = VK_F8;
    constexpr int kStealVirtualKeyR = 'R';
    constexpr int kStealVirtualKeyG = 'G';
    constexpr int kPollMs = 20;
    constexpr DWORD kActiveWindowMs = 5000;
    constexpr SIZE_T kAllocSearchRange = 0x70000000;
    constexpr SIZE_T kAllocStep = 0x10000;

    struct HookSpec
    {
        const char* name;
        uintptr_t offset;
        const unsigned char* originalBytes;
        SIZE_T originalLength;
        const unsigned char* caveTemplate;
        SIZE_T caveTemplateLength;
        SIZE_T overwriteLength;
    };

    struct HookRuntime
    {
        const HookSpec* spec;
        unsigned char patchBytes[16];
        void* targetAddress;
        void* caveAddress;
        bool active;
    };

    const unsigned char kMirrorPlus10Original[] = { 0x48, 0x89, 0x48, 0x10, 0x89, 0x70, 0x0C };
    const unsigned char kMirrorPlus48Original[] = { 0x48, 0x89, 0x43, 0x48, 0x48, 0x8B, 0x46, 0x50 };

    const unsigned char kMirrorPlus10Cave[] =
    {
        0x3B, 0x48, 0x10,
        0x7C, 0x04,
        0x48, 0x89, 0x48, 0x10,
        0x89, 0x70, 0x0C,
        0xE9, 0x00, 0x00, 0x00, 0x00,
    };

    const unsigned char kMirrorPlus48Cave[] =
    {
        0x3B, 0x43, 0x48,
        0x7C, 0x04,
        0x48, 0x89, 0x43, 0x48,
        0x48, 0x8B, 0x46, 0x50,
        0xE9, 0x00, 0x00, 0x00, 0x00,
    };

    const HookSpec kHookSpecs[] =
    {
        { "mirror_plus_10", 0x1B37BCE, kMirrorPlus10Original, sizeof(kMirrorPlus10Original), kMirrorPlus10Cave, sizeof(kMirrorPlus10Cave), sizeof(kMirrorPlus10Original) },
        { "mirror_plus_48", 0x12ED186, kMirrorPlus48Original, sizeof(kMirrorPlus48Original), kMirrorPlus48Cave, sizeof(kMirrorPlus48Cave), sizeof(kMirrorPlus48Original) },
    };
    constexpr SIZE_T kHookCount = sizeof(kHookSpecs) / sizeof(kHookSpecs[0]);

    HookRuntime g_hooks[kHookCount] = {};
    HANDLE g_thread = nullptr;
    volatile LONG g_running = 1;

    bool IsTargetProcess()
    {
        wchar_t path[MAX_PATH] = {};
        DWORD len = GetModuleFileNameW(nullptr, path, static_cast<DWORD>(sizeof(path) / sizeof(path[0])));
        if (len == 0 || len >= static_cast<DWORD>(sizeof(path) / sizeof(path[0])))
        {
            return false;
        }

        const wchar_t* fileName = path;
        for (DWORD i = 0; i < len; ++i)
        {
            if (path[i] == L'\\' || path[i] == L'/')
            {
                fileName = path + i + 1;
            }
        }

        return _wcsicmp(fileName, kTargetExe) == 0;
    }

    bool IsProcessFocused()
    {
        HWND hwnd = GetForegroundWindow();
        if (!hwnd)
        {
            return false;
        }

        DWORD pid = 0;
        GetWindowThreadProcessId(hwnd, &pid);
        return pid == GetCurrentProcessId();
    }

    bool AnyHookActive()
    {
        for (SIZE_T i = 0; i < kHookCount; ++i)
        {
            if (g_hooks[i].active)
            {
                return true;
            }
        }

        return false;
    }

    bool WriteMemory(void* target, const void* data, SIZE_T length)
    {
        DWORD oldProtect = 0;
        if (!VirtualProtect(target, length, PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            return false;
        }

        std::memcpy(target, data, length);
        FlushInstructionCache(GetCurrentProcess(), target, length);

        DWORD ignored = 0;
        VirtualProtect(target, length, oldProtect, &ignored);
        return true;
    }

    void WriteRel32(unsigned char* buffer, SIZE_T offset, intptr_t value)
    {
        std::int32_t rel = static_cast<std::int32_t>(value);
        std::memcpy(buffer + offset, &rel, sizeof(rel));
    }

    bool FitsRel32(uintptr_t sourceAfterJmp, uintptr_t destination)
    {
        intptr_t diff = static_cast<intptr_t>(destination) - static_cast<intptr_t>(sourceAfterJmp);
        return diff >= INT32_MIN && diff <= INT32_MAX;
    }

    void* TryAllocateAt(uintptr_t address, SIZE_T size)
    {
        if (address == 0)
        {
            return nullptr;
        }

        uintptr_t aligned = address & ~static_cast<uintptr_t>(0xFFFF);
        return VirtualAlloc(reinterpret_cast<void*>(aligned), size, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
    }

    void* AllocateNear(uintptr_t targetAddress, SIZE_T size)
    {
        for (SIZE_T distance = 0; distance < kAllocSearchRange; distance += kAllocStep)
        {
            void* ptr = TryAllocateAt(targetAddress + distance, size);
            if (ptr && FitsRel32(targetAddress + 5, reinterpret_cast<uintptr_t>(ptr)))
            {
                return ptr;
            }

            if (distance == 0)
            {
                continue;
            }

            ptr = TryAllocateAt(targetAddress - distance, size);
            if (ptr && FitsRel32(targetAddress + 5, reinterpret_cast<uintptr_t>(ptr)))
            {
                return ptr;
            }
        }

        return nullptr;
    }

    bool PrepareHook(HookRuntime& runtime, const HookSpec& spec, uintptr_t moduleBase)
    {
        runtime = {};
        runtime.spec = &spec;
        runtime.targetAddress = reinterpret_cast<void*>(moduleBase + spec.offset);
        runtime.active = false;

        if (std::memcmp(runtime.targetAddress, spec.originalBytes, spec.originalLength) != 0)
        {
            return false;
        }

        runtime.caveAddress = AllocateNear(reinterpret_cast<uintptr_t>(runtime.targetAddress), spec.caveTemplateLength + 16);
        if (!runtime.caveAddress)
        {
            return false;
        }

        unsigned char caveBytes[64] = {};
        std::memcpy(caveBytes, spec.caveTemplate, spec.caveTemplateLength);

        const uintptr_t caveBase = reinterpret_cast<uintptr_t>(runtime.caveAddress);
        const uintptr_t returnAddress = reinterpret_cast<uintptr_t>(runtime.targetAddress) + spec.overwriteLength;
        const uintptr_t jumpInstructionEnd = caveBase + spec.caveTemplateLength;
        WriteRel32(caveBytes, spec.caveTemplateLength - 4, static_cast<intptr_t>(returnAddress) - static_cast<intptr_t>(jumpInstructionEnd));

        if (!WriteMemory(runtime.caveAddress, caveBytes, spec.caveTemplateLength))
        {
            return false;
        }

        std::memset(runtime.patchBytes, 0x90, sizeof(runtime.patchBytes));
        runtime.patchBytes[0] = 0xE9;
        const uintptr_t targetJmpEnd = reinterpret_cast<uintptr_t>(runtime.targetAddress) + 5;
        WriteRel32(runtime.patchBytes, 1, static_cast<intptr_t>(caveBase) - static_cast<intptr_t>(targetJmpEnd));
        return true;
    }

    bool SetHookState(HookRuntime& runtime, bool enable)
    {
        if (runtime.active == enable)
        {
            return true;
        }

        const unsigned char* bytes = enable ? runtime.patchBytes : runtime.spec->originalBytes;
        const SIZE_T length = enable ? runtime.spec->overwriteLength : runtime.spec->originalLength;
        if (!WriteMemory(runtime.targetAddress, bytes, length))
        {
            return false;
        }

        runtime.active = enable;
        return true;
    }

    DWORD WINAPI GuardThreadMain(void*)
    {
        if (!IsTargetProcess())
        {
            return 0;
        }

        uintptr_t moduleBase = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
        for (SIZE_T i = 0; i < kHookCount; ++i)
        {
            if (!PrepareHook(g_hooks[i], kHookSpecs[i], moduleBase))
            {
                return 0;
            }
        }

        ULONGLONG activeUntil = 0;
        while (InterlockedCompareExchange(&g_running, g_running, g_running))
        {
            if (GetAsyncKeyState(kExitVirtualKey) & 1)
            {
                break;
            }

            const bool focused = IsProcessFocused();
            const bool stealPressed = focused &&
                ((GetAsyncKeyState(kStealVirtualKeyR) & 1) || (GetAsyncKeyState(kStealVirtualKeyG) & 1));

            if (stealPressed)
            {
                activeUntil = GetTickCount64() + kActiveWindowMs;
                for (SIZE_T i = 0; i < kHookCount; ++i)
                {
                    if (!SetHookState(g_hooks[i], true))
                    {
                        InterlockedExchange(&g_running, 0);
                        break;
                    }
                }
            }
            else if (AnyHookActive() && GetTickCount64() > activeUntil)
            {
                for (SIZE_T i = 0; i < kHookCount; ++i)
                {
                    SetHookState(g_hooks[i], false);
                }
            }

            Sleep(kPollMs);
        }

        for (SIZE_T i = 0; i < kHookCount; ++i)
        {
            SetHookState(g_hooks[i], false);
        }

        return 0;
    }
}

extern "C" __declspec(dllexport) int ContributionGuardVersion()
{
    return 1;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(module);
        InterlockedExchange(&g_running, 1);
        g_thread = CreateThread(nullptr, 0, GuardThreadMain, nullptr, 0, nullptr);
        if (g_thread)
        {
            CloseHandle(g_thread);
            g_thread = nullptr;
        }
        break;
    case DLL_PROCESS_DETACH:
        InterlockedExchange(&g_running, 0);
        break;
    default:
        break;
    }

    return TRUE;
}
