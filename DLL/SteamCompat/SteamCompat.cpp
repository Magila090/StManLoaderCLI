#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <iterator>
#include <string>
#include <vector>

// ============================================================
// SteamCompat.dll — минимальная версия для загрузки через Proxy/winmm.
//
// Оставлено только то, что нужно рабочей схеме:
//   - Steam environment = 480;
//   - запоминание настоящего AppID игры;
//   - блокировка перезаписи SteamAppId / SteamGameId;
//   - SteamAPI_RestartAppIfNecessary -> false;
//   - SteamAPI_ISteamUtils_GetAppID -> настоящий AppID игры;
//   - перехват Steam Init / GetProcAddress / LoadLibrary через IAT.
//
// Steam Overlay теперь загружается очень рано самим Proxy/winmm,
// поэтому SteamCompat повторно его не загружает.
//
// Собирать: Release | x64.
// ============================================================

namespace
{
    constexpr DWORD kSteamContextAppId = 480;
    constexpr wchar_t kSteamContextAppIdW[] = L"480";
    constexpr char kSteamContextAppIdA[] = "480";

    HMODULE g_self = nullptr;
    INIT_ONCE g_initOnce = INIT_ONCE_STATIC_INIT;
    volatile LONG g_stop = FALSE;
    volatile LONG g_gameAppId = 0;

    std::wstring g_gameDirectory;

    // ------------------------------------------------------------
    // ������������ WinAPI �������.
    // �������� Windows �� �� ������ � ������ ������ IAT ������� DLL.
    // ������� ��� ��������� ������ ����� � ��������� Kernel32/KernelBase.
    // ------------------------------------------------------------

    using SetEnvironmentVariableW_t = BOOL(WINAPI*)(LPCWSTR, LPCWSTR);
    using SetEnvironmentVariableA_t = BOOL(WINAPI*)(LPCSTR, LPCSTR);
    using GetProcAddress_t = FARPROC(WINAPI*)(HMODULE, LPCSTR);
    using LoadLibraryW_t = HMODULE(WINAPI*)(LPCWSTR);
    using LoadLibraryA_t = HMODULE(WINAPI*)(LPCSTR);
    using LoadLibraryExW_t = HMODULE(WINAPI*)(LPCWSTR, HANDLE, DWORD);
    using LoadLibraryExA_t = HMODULE(WINAPI*)(LPCSTR, HANDLE, DWORD);

    SetEnvironmentVariableW_t g_realSetEnvironmentVariableW = nullptr;
    SetEnvironmentVariableA_t g_realSetEnvironmentVariableA = nullptr;
    GetProcAddress_t g_realGetProcAddress = nullptr;
    LoadLibraryW_t g_realLoadLibraryW = nullptr;
    LoadLibraryA_t g_realLoadLibraryA = nullptr;
    LoadLibraryExW_t g_realLoadLibraryExW = nullptr;
    LoadLibraryExA_t g_realLoadLibraryExA = nullptr;

    // ------------------------------------------------------------
    // ������������ Steam API �������.
    // ------------------------------------------------------------

    using SteamGetAppId_t = std::uint32_t(__cdecl*)(void* self);
    using SteamInternalInit_t = int(__cdecl*)(const char* versions, void* outError);
    using SteamInitFlat_t = int(__cdecl*)(void* outError);
    using SteamInitBool_t = bool(__cdecl*)();

    SteamGetAppId_t g_realSteamGetAppId = nullptr;
    SteamInternalInit_t g_realSteamInternalInit = nullptr;
    SteamInitFlat_t g_realSteamInitFlat = nullptr;
    SteamInitBool_t g_realSteamInit = nullptr;
    SteamInitBool_t g_realSteamInitSafe = nullptr;
    HMODULE g_steamApiModule = nullptr;

    SRWLOCK g_steamResolveLock = SRWLOCK_INIT;
    SRWLOCK g_patchLock = SRWLOCK_INIT;

    // ============================================================
    // Пути
    // ============================================================

    std::wstring DirectoryOfPath(const std::wstring& path)
    {
        const size_t pos = path.find_last_of(L"\\/");

        if (pos == std::wstring::npos)
            return {};

        return path.substr(0, pos + 1);
    }

    std::wstring GetModulePath(HMODULE module)
    {
        std::vector<wchar_t> path(32768);

        const DWORD length = GetModuleFileNameW(
            module,
            path.data(),
            static_cast<DWORD>(path.size())
        );

        if (length == 0 || length >= path.size())
            return {};

        return std::wstring(path.data(), length);
    }

    std::wstring GetExecutableDirectory()
    {
        return DirectoryOfPath(GetModulePath(nullptr));
    }


    bool StartsWithInsensitive(const std::wstring& text, const std::wstring& prefix)
    {
        if (prefix.empty() || text.size() < prefix.size())
            return false;

        return _wcsnicmp(text.c_str(), prefix.c_str(), prefix.size()) == 0;
    }

    // ============================================================
    // AppID
    // ============================================================

    void RememberGameAppId(DWORD appId)
    {
        if (appId == 0 || appId == kSteamContextAppId)
            return;

        InterlockedExchange(
            &g_gameAppId,
            static_cast<LONG>(appId)
        );
    }

    void RememberGameAppId(const wchar_t* value)
    {
        if (value == nullptr || *value == L'\0')
            return;

        wchar_t* end = nullptr;
        const unsigned long parsed = wcstoul(value, &end, 10);

        if (end == value || *end != L'\0')
            return;

        RememberGameAppId(static_cast<DWORD>(parsed));
    }

    void RememberGameAppId(const char* value)
    {
        if (value == nullptr || *value == '\0')
            return;

        char* end = nullptr;
        const unsigned long parsed = strtoul(value, &end, 10);

        if (end == value || *end != '\0')
            return;

        RememberGameAppId(static_cast<DWORD>(parsed));
    }

    DWORD CurrentGameAppId()
    {
        return static_cast<DWORD>(InterlockedCompareExchange(&g_gameAppId, 0, 0));
    }

    bool IsSteamIdentityVariableW(LPCWSTR name)
    {
        if (name == nullptr)
            return false;

        return lstrcmpiW(name, L"SteamAppId") == 0 ||
            lstrcmpiW(name, L"SteamGameId") == 0;
    }

    bool IsSteamIdentityVariableA(LPCSTR name)
    {
        if (name == nullptr)
            return false;

        return lstrcmpiA(name, "SteamAppId") == 0 ||
            lstrcmpiA(name, "SteamGameId") == 0;
    }

    void ResolveWindowsApis()
    {
        HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");

        if (kernel32 == nullptr)
            return;

        g_realGetProcAddress = reinterpret_cast<GetProcAddress_t>(
            ::GetProcAddress(kernel32, "GetProcAddress")
            );

        g_realLoadLibraryW = reinterpret_cast<LoadLibraryW_t>(
            ::GetProcAddress(kernel32, "LoadLibraryW")
            );

        g_realLoadLibraryA = reinterpret_cast<LoadLibraryA_t>(
            ::GetProcAddress(kernel32, "LoadLibraryA")
            );

        g_realLoadLibraryExW = reinterpret_cast<LoadLibraryExW_t>(
            ::GetProcAddress(kernel32, "LoadLibraryExW")
            );

        g_realLoadLibraryExA = reinterpret_cast<LoadLibraryExA_t>(
            ::GetProcAddress(kernel32, "LoadLibraryExA")
            );

        // �� ����������� Windows Kernel32 ������������/��������� ��� �������.
        g_realSetEnvironmentVariableW = reinterpret_cast<SetEnvironmentVariableW_t>(
            ::GetProcAddress(kernel32, "SetEnvironmentVariableW")
            );

        g_realSetEnvironmentVariableA = reinterpret_cast<SetEnvironmentVariableA_t>(
            ::GetProcAddress(kernel32, "SetEnvironmentVariableA")
            );
    }

    void CaptureAppIdFromEnvironment()
    {
        wchar_t buffer[64]{};

        DWORD n = GetEnvironmentVariableW(
            L"SteamAppId",
            buffer,
            static_cast<DWORD>(std::size(buffer))
        );

        if (n > 0 && n < std::size(buffer))
            RememberGameAppId(buffer);

        n = GetEnvironmentVariableW(
            L"SteamGameId",
            buffer,
            static_cast<DWORD>(std::size(buffer))
        );

        if (n > 0 && n < std::size(buffer))
            RememberGameAppId(buffer);
    }

    void ForceSteamEnvironment()
    {
        if (g_realSetEnvironmentVariableW == nullptr)
            return;

        g_realSetEnvironmentVariableW(L"SteamClientLaunch", L"1");
        g_realSetEnvironmentVariableW(L"SteamGameId", kSteamContextAppIdW);
        g_realSetEnvironmentVariableW(L"SteamAppId", kSteamContextAppIdW);
        g_realSetEnvironmentVariableW(L"SteamOverlayGameId", kSteamContextAppIdW);
        g_realSetEnvironmentVariableW(L"SteamEnv", L"1");
    }

    // ============================================================
    // Steam API originals
    // ============================================================

    void ResolveSteamApiOriginals()
    {
        if (g_realGetProcAddress == nullptr)
            return;

        HMODULE steam = GetModuleHandleW(L"steam_api64.dll");

        if (steam == nullptr)
            return;

        AcquireSRWLockExclusive(&g_steamResolveLock);

        if (g_steamApiModule != steam)
        {
            g_steamApiModule = steam;

            g_realSteamGetAppId = reinterpret_cast<SteamGetAppId_t>(
                g_realGetProcAddress(steam, "SteamAPI_ISteamUtils_GetAppID")
                );

            g_realSteamInternalInit = reinterpret_cast<SteamInternalInit_t>(
                g_realGetProcAddress(steam, "SteamInternal_SteamAPI_Init")
                );

            g_realSteamInitFlat = reinterpret_cast<SteamInitFlat_t>(
                g_realGetProcAddress(steam, "SteamAPI_InitFlat")
                );

            g_realSteamInit = reinterpret_cast<SteamInitBool_t>(
                g_realGetProcAddress(steam, "SteamAPI_Init")
                );

            g_realSteamInitSafe = reinterpret_cast<SteamInitBool_t>(
                g_realGetProcAddress(steam, "SteamAPI_InitSafe")
                );
        }

        ReleaseSRWLockExclusive(&g_steamResolveLock);
    }

    // ============================================================
    // ���� replacements
    // ============================================================

    BOOL WINAPI HookSetEnvironmentVariableW(LPCWSTR name, LPCWSTR value)
    {
        if (IsSteamIdentityVariableW(name) && value != nullptr)
        {
            RememberGameAppId(value);

            if (g_realSetEnvironmentVariableW != nullptr)
                return g_realSetEnvironmentVariableW(name, kSteamContextAppIdW);

            return FALSE;
        }

        // NULL означает удалить переменную. Не мешаем Steam делать cleanup.
        if (g_realSetEnvironmentVariableW != nullptr)
            return g_realSetEnvironmentVariableW(name, value);

        return FALSE;
    }

    BOOL WINAPI HookSetEnvironmentVariableA(LPCSTR name, LPCSTR value)
    {
        if (IsSteamIdentityVariableA(name) && value != nullptr)
        {
            RememberGameAppId(value);

            if (g_realSetEnvironmentVariableA != nullptr)
                return g_realSetEnvironmentVariableA(name, kSteamContextAppIdA);

            return FALSE;
        }

        // NULL означает удалить переменную. Не мешаем Steam делать cleanup.
        if (g_realSetEnvironmentVariableA != nullptr)
            return g_realSetEnvironmentVariableA(name, value);

        return FALSE;
    }

    bool __cdecl HookSteamRestart(std::uint32_t appId)
    {
        RememberGameAppId(appId);
        ForceSteamEnvironment();

        // ������ ���������� restartSkipOriginal=true + restartForceFalse=true.
        return false;
    }

    std::uint32_t __cdecl HookSteamGetAppId(void* self)
    {
        const DWORD gameAppId = CurrentGameAppId();

        if (gameAppId != 0)
            return gameAppId;

        ResolveSteamApiOriginals();

        if (g_realSteamGetAppId != nullptr)
            return g_realSteamGetAppId(self);

        return kSteamContextAppId;
    }

    int __cdecl HookSteamInternalInit(const char* versions, void* outError)
    {
        // ���� ���� ������ �������� ���� ��������� AppID �� ����, ��� ���
        // SetEnvironmentVariable hook ��� ����������, �� ����� ��� �������.
        CaptureAppIdFromEnvironment();

        // ����������� ����� ��� Machine Party: ��������������� ����� Init
        // Steam ������ ������ ������ 480.
        ForceSteamEnvironment();

        ResolveSteamApiOriginals();

        if (g_realSteamInternalInit != nullptr)
            return g_realSteamInternalInit(versions, outError);

        // k_ESteamAPIInitResult_FailedGeneric
        return 1;
    }

    int __cdecl HookSteamInitFlat(void* outError)
    {
        CaptureAppIdFromEnvironment();
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();

        if (g_realSteamInitFlat != nullptr)
            return g_realSteamInitFlat(outError);

        return 1;
    }

    bool __cdecl HookSteamInit()
    {
        CaptureAppIdFromEnvironment();
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();

        if (g_realSteamInit != nullptr)
            return g_realSteamInit();

        return false;
    }

    bool __cdecl HookSteamInitSafe()
    {
        CaptureAppIdFromEnvironment();
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();

        if (g_realSteamInitSafe != nullptr)
            return g_realSteamInitSafe();

        return false;
    }

    // ============================================================
    // �������� HMODULE
    // ============================================================

    std::wstring BaseNameOfModule(HMODULE module)
    {
        const std::wstring full = GetModulePath(module);

        if (full.empty())
            return {};

        const size_t pos = full.find_last_of(L"\\/");

        if (pos == std::wstring::npos)
            return full;

        return full.substr(pos + 1);
    }

    bool IsSteamApiModule(HMODULE module)
    {
        if (module == nullptr)
            return false;

        const std::wstring name = BaseNameOfModule(module);
        return !name.empty() && _wcsicmp(name.c_str(), L"steam_api64.dll") == 0;
    }

    // ============================================================
    // GetProcAddress / LoadLibrary hooks
    // ============================================================

    FARPROC WINAPI HookGetProcAddress(HMODULE module, LPCSTR procName)
    {
        if (g_realGetProcAddress == nullptr || procName == nullptr)
            return nullptr;

        FARPROC result = g_realGetProcAddress(module, procName);

        // Ordinal, а не строковое имя.
        if ((reinterpret_cast<ULONG_PTR>(procName) >> 16) == 0)
            return result;

        if (IsSteamApiModule(module))
        {
            if (std::strcmp(procName, "SteamAPI_RestartAppIfNecessary") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamRestart);

            if (std::strcmp(procName, "SteamAPI_ISteamUtils_GetAppID") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamGetAppId);

            if (std::strcmp(procName, "SteamInternal_SteamAPI_Init") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamInternalInit);

            if (std::strcmp(procName, "SteamAPI_InitFlat") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamInitFlat);

            if (std::strcmp(procName, "SteamAPI_Init") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamInit);

            if (std::strcmp(procName, "SteamAPI_InitSafe") == 0)
                return reinterpret_cast<FARPROC>(&HookSteamInitSafe);
        }

        const std::wstring moduleName = BaseNameOfModule(module);

        if (_wcsicmp(moduleName.c_str(), L"kernel32.dll") == 0 ||
            _wcsicmp(moduleName.c_str(), L"kernelbase.dll") == 0)
        {
            if (std::strcmp(procName, "SetEnvironmentVariableW") == 0)
                return reinterpret_cast<FARPROC>(&HookSetEnvironmentVariableW);

            if (std::strcmp(procName, "SetEnvironmentVariableA") == 0)
                return reinterpret_cast<FARPROC>(&HookSetEnvironmentVariableA);
        }

        return result;
    }

    void PatchModuleImports(HMODULE module);
    void PatchGameModules();

    bool ShouldPatchModuleHandle(HMODULE module)
    {
        if (module == nullptr || module == g_self)
            return false;

        if (g_gameDirectory.empty())
            return true;

        const std::wstring path = GetModulePath(module);

        if (path.empty())
            return false;

        return StartsWithInsensitive(path, g_gameDirectory);
    }

    void AfterLibraryLoad(HMODULE module)
    {
        if (module == nullptr)
            return;

        ResolveSteamApiOriginals();

        // Вместо полного снимка всех модулей патчим только что загруженную DLL.
        if (ShouldPatchModuleHandle(module))
        {
            AcquireSRWLockExclusive(&g_patchLock);
            PatchModuleImports(module);
            ReleaseSRWLockExclusive(&g_patchLock);
        }
    }

    HMODULE WINAPI HookLoadLibraryW(LPCWSTR fileName)
    {
        if (g_realLoadLibraryW == nullptr)
            return nullptr;

        HMODULE module = g_realLoadLibraryW(fileName);
        AfterLibraryLoad(module);
        return module;
    }

    HMODULE WINAPI HookLoadLibraryA(LPCSTR fileName)
    {
        if (g_realLoadLibraryA == nullptr)
            return nullptr;

        HMODULE module = g_realLoadLibraryA(fileName);
        AfterLibraryLoad(module);
        return module;
    }

    HMODULE WINAPI HookLoadLibraryExW(LPCWSTR fileName, HANDLE file, DWORD flags)
    {
        if (g_realLoadLibraryExW == nullptr)
            return nullptr;

        HMODULE module = g_realLoadLibraryExW(fileName, file, flags);
        AfterLibraryLoad(module);
        return module;
    }

    HMODULE WINAPI HookLoadLibraryExA(LPCSTR fileName, HANDLE file, DWORD flags)
    {
        if (g_realLoadLibraryExA == nullptr)
            return nullptr;

        HMODULE module = g_realLoadLibraryExA(fileName, file, flags);
        AfterLibraryLoad(module);
        return module;
    }

    // ============================================================
    // IAT patching
    // ============================================================

    void* ReplacementForImport(const char* name)
    {
        if (name == nullptr)
            return nullptr;

        if (std::strcmp(name, "SetEnvironmentVariableW") == 0)
            return reinterpret_cast<void*>(&HookSetEnvironmentVariableW);

        if (std::strcmp(name, "SetEnvironmentVariableA") == 0)
            return reinterpret_cast<void*>(&HookSetEnvironmentVariableA);

        if (std::strcmp(name, "GetProcAddress") == 0)
            return reinterpret_cast<void*>(&HookGetProcAddress);

        if (std::strcmp(name, "LoadLibraryW") == 0)
            return reinterpret_cast<void*>(&HookLoadLibraryW);

        if (std::strcmp(name, "LoadLibraryA") == 0)
            return reinterpret_cast<void*>(&HookLoadLibraryA);

        if (std::strcmp(name, "LoadLibraryExW") == 0)
            return reinterpret_cast<void*>(&HookLoadLibraryExW);

        if (std::strcmp(name, "LoadLibraryExA") == 0)
            return reinterpret_cast<void*>(&HookLoadLibraryExA);

        if (std::strcmp(name, "SteamAPI_RestartAppIfNecessary") == 0)
            return reinterpret_cast<void*>(&HookSteamRestart);

        if (std::strcmp(name, "SteamAPI_ISteamUtils_GetAppID") == 0)
            return reinterpret_cast<void*>(&HookSteamGetAppId);

        if (std::strcmp(name, "SteamInternal_SteamAPI_Init") == 0)
            return reinterpret_cast<void*>(&HookSteamInternalInit);

        if (std::strcmp(name, "SteamAPI_InitFlat") == 0)
            return reinterpret_cast<void*>(&HookSteamInitFlat);

        if (std::strcmp(name, "SteamAPI_Init") == 0)
            return reinterpret_cast<void*>(&HookSteamInit);

        if (std::strcmp(name, "SteamAPI_InitSafe") == 0)
            return reinterpret_cast<void*>(&HookSteamInitSafe);

        return nullptr;
    }

    bool WriteIatPointer(ULONG_PTR* slot, void* replacement)
    {
        if (slot == nullptr || replacement == nullptr)
            return false;

        if (*slot == reinterpret_cast<ULONG_PTR>(replacement))
            return true;

        DWORD oldProtect = 0;

        if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &oldProtect))
            return false;

#ifdef _WIN64
        InterlockedExchange64(
            reinterpret_cast<volatile LONG64*>(slot),
            static_cast<LONG64>(reinterpret_cast<ULONG_PTR>(replacement))
        );
#else
        InterlockedExchange(
            reinterpret_cast<volatile LONG*>(slot),
            static_cast<LONG>(reinterpret_cast<ULONG_PTR>(replacement))
        );
#endif

        DWORD ignored = 0;
        VirtualProtect(slot, sizeof(void*), oldProtect, &ignored);
        FlushInstructionCache(GetCurrentProcess(), slot, sizeof(void*));

        return true;
    }

    void PatchModuleImports(HMODULE module)
    {
        if (module == nullptr || module == g_self)
            return;

        __try
        {
            auto* base = reinterpret_cast<std::uint8_t*>(module);
            auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);

            if (dos->e_magic != IMAGE_DOS_SIGNATURE)
                return;

            auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);

            if (nt->Signature != IMAGE_NT_SIGNATURE)
                return;

            const IMAGE_DATA_DIRECTORY& directory =
                nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];

            if (directory.VirtualAddress == 0 || directory.Size == 0)
                return;

            auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(
                base + directory.VirtualAddress
                );

            for (; descriptor->Name != 0; ++descriptor)
            {
                if (descriptor->FirstThunk == 0 || descriptor->OriginalFirstThunk == 0)
                    continue;

                auto* originalThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(
                    base + descriptor->OriginalFirstThunk
                    );

                auto* firstThunk = reinterpret_cast<IMAGE_THUNK_DATA*>(
                    base + descriptor->FirstThunk
                    );

                for (; originalThunk->u1.AddressOfData != 0;
                    ++originalThunk, ++firstThunk)
                {
#ifdef _WIN64
                    if (IMAGE_SNAP_BY_ORDINAL64(originalThunk->u1.Ordinal))
                        continue;
#else
                    if (IMAGE_SNAP_BY_ORDINAL32(originalThunk->u1.Ordinal))
                        continue;
#endif

                    auto* byName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(
                        base + originalThunk->u1.AddressOfData
                        );

                    const char* functionName =
                        reinterpret_cast<const char*>(byName->Name);

                    void* replacement = ReplacementForImport(functionName);

                    if (replacement == nullptr)
                        continue;

                    WriteIatPointer(
                        reinterpret_cast<ULONG_PTR*>(&firstThunk->u1.Function),
                        replacement
                    );
                }
            }
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            // DLL ����� ����������� �� ����� ������������. ������ ���������� �.
        }
    }

    bool ShouldPatchModule(const MODULEENTRY32W& entry)
    {
        if (entry.hModule == nullptr || entry.hModule == g_self)
            return false;

        if (g_gameDirectory.empty())
            return true;

        return StartsWithInsensitive(entry.szExePath, g_gameDirectory);
    }

    void PatchGameModules()
    {
        AcquireSRWLockExclusive(&g_patchLock);

        HANDLE snapshot = CreateToolhelp32Snapshot(
            TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
            GetCurrentProcessId()
        );

        if (snapshot == INVALID_HANDLE_VALUE)
        {
            ReleaseSRWLockExclusive(&g_patchLock);
            return;
        }

        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);

        if (Module32FirstW(snapshot, &entry))
        {
            do
            {
                if (ShouldPatchModule(entry))
                    PatchModuleImports(entry.hModule);
            } while (Module32NextW(snapshot, &entry));
        }

        CloseHandle(snapshot);
        ReleaseSRWLockExclusive(&g_patchLock);
    }

    // ============================================================
    // Короткий fallback-сканер
    // ============================================================

    DWORD WINAPI ScannerThread(LPVOID)
    {
        // Основной путь — событийный: HookLoadLibrary* патчит новую DLL
        // сразу после её загрузки. Этот поток нужен только как fallback
        // для нестандартных загрузчиков и живёт всего 10 секунд.
        for (int i = 0;
            i < 100 && InterlockedCompareExchange(&g_stop, 0, 0) == FALSE;
            ++i)
        {
            ResolveSteamApiOriginals();
            PatchGameModules();
            Sleep(100);
        }

        return 0;
    }

    // ============================================================
    // Основная инициализация
    // ============================================================

    BOOL CALLBACK InitializeOnce(PINIT_ONCE, PVOID, PVOID*)
    {
        ResolveWindowsApis();

        g_gameDirectory = GetExecutableDirectory();

        if (!g_gameDirectory.empty())
            SetCurrentDirectoryW(g_gameDirectory.c_str());

        // Сначала запоминаем возможный AppID игры, затем возвращаем
        // реальный Steam-контекст процесса к 480.
        CaptureAppIdFromEnvironment();
        ForceSteamEnvironment();

        ResolveSteamApiOriginals();
        PatchGameModules();

        // Небольшой временный fallback. После 10 секунд поток завершится.
        // Поздние обычные LoadLibrary* всё равно ловятся IAT-хуками.
        HANDLE thread = CreateThread(
            nullptr,
            0,
            ScannerThread,
            nullptr,
            0,
            nullptr
        );

        if (thread != nullptr)
            CloseHandle(thread);

        return TRUE;
    }

}

// ============================================================
// ���� export �������� ���� ������������ Proxy.dll loader.
// ============================================================
extern "C" __declspec(dllexport)
void InitializeProxyModule()
{
    InitOnceExecuteOnce(
        &g_initOnce,
        InitializeOnce,
        nullptr,
        nullptr
    );
}

// ============================================================
// ����� DLL ����� ������������������ ���� ��� ������� LoadLibrary/inject.
// ������ ������ �� DllMain �� ������ � ������ ��������� �����.
// ============================================================
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;
        DisableThreadLibraryCalls(module);

        // Инициализацию здесь не запускаем:
        // Proxy после LoadLibrary вызывает InitializeProxyModule().
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        InterlockedExchange(&g_stop, TRUE);
    }

    return TRUE;
}