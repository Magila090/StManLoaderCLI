#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <tlhelp32.h>

#include <cstdint>
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iterator>
#include <string>
#include <vector>

// ============================================================
// SteamCompat.dll
//
// Нативный перенос РАБОЧЕЙ логики из:
//   frida_controller_env.py
//   hook_min_gud_3.js
//
// Предназначен для загрузки твоим Proxy/WINMM через:
//   InitializeProxyModule()
//
// x64 / MSVC.
// ============================================================

namespace
{
    HMODULE g_self = nullptr;

    INIT_ONCE g_initOnce = INIT_ONCE_STATIC_INIT;
    SRWLOCK g_patchLock = SRWLOCK_INIT;
    SRWLOCK g_resolveLock = SRWLOCK_INIT;

    volatile LONG g_stop = FALSE;

    std::wstring g_gameDirectory;

    // Значения по умолчанию соответствуют текущему How to Fish.
    // Если найден appid_config.txt, они заменяются значениями из него.
    constexpr DWORD kDefaultRealAppId = 4001890;
    constexpr DWORD kDefaultFakeAppId = 480;

    // Cached text forms: no heap allocation in SetEnvironmentVariable hooks.
    wchar_t g_fakeAppIdW[16] = L"480";
    char g_fakeAppIdA[16] = "480";

    // ------------------------------------------------------------
    // Настоящие WinAPI.
    // ------------------------------------------------------------

    using SetEnvironmentVariableW_t =
        BOOL(WINAPI*)(LPCWSTR, LPCWSTR);

    using SetEnvironmentVariableA_t =
        BOOL(WINAPI*)(LPCSTR, LPCSTR);

    using GetProcAddress_t =
        FARPROC(WINAPI*)(HMODULE, LPCSTR);

    using LoadLibraryW_t =
        HMODULE(WINAPI*)(LPCWSTR);

    using LoadLibraryA_t =
        HMODULE(WINAPI*)(LPCSTR);

    using LoadLibraryExW_t =
        HMODULE(WINAPI*)(LPCWSTR, HANDLE, DWORD);

    using LoadLibraryExA_t =
        HMODULE(WINAPI*)(LPCSTR, HANDLE, DWORD);

    SetEnvironmentVariableW_t g_realSetEnvironmentVariableW = nullptr;
    SetEnvironmentVariableA_t g_realSetEnvironmentVariableA = nullptr;
    GetProcAddress_t g_realGetProcAddress = nullptr;
    LoadLibraryW_t g_realLoadLibraryW = nullptr;
    LoadLibraryA_t g_realLoadLibraryA = nullptr;
    LoadLibraryExW_t g_realLoadLibraryExW = nullptr;
    LoadLibraryExA_t g_realLoadLibraryExA = nullptr;

    // ------------------------------------------------------------
    // Steam API originals.
    // ------------------------------------------------------------

    using SteamInternalInit_t =
        int(__cdecl*)(const char*, void*);

    using SteamInitFlat_t =
        int(__cdecl*)(void*);

    using SteamInitBool_t =
        bool(__cdecl*)();

    using SteamGetHSteamPipe_t =
        int(__cdecl*)();

    using SteamGetHSteamUser_t =
        int(__cdecl*)();

    SteamInternalInit_t g_realSteamInternalInit = nullptr;
    SteamInitFlat_t g_realSteamInitFlat = nullptr;
    SteamInitBool_t g_realSteamInit = nullptr;
    SteamInitBool_t g_realSteamInitSafe = nullptr;

    SteamGetHSteamPipe_t g_realGetHSteamPipe = nullptr;
    SteamGetHSteamUser_t g_realGetHSteamUser = nullptr;

    HMODULE g_steamApiModule = nullptr;

    // ------------------------------------------------------------
    // steamclient64.dll
    // ------------------------------------------------------------

    using CreateInterface_t =
        void* (__cdecl*)(const char*, int*);

    using SteamClientGetUtils_t =
        void* (__fastcall*)(void*, int, const char*);

    using SteamClientGetGeneric_t =
        void* (__fastcall*)(void*, int, int, const char*);

    CreateInterface_t g_realCreateInterface = nullptr;

    SteamClientGetUtils_t g_realSteamClientGetUtils = nullptr;
    SteamClientGetGeneric_t g_realSteamClientGetGeneric = nullptr;

    struct SteamClientVtableRecord
    {
        void* object = nullptr;
        void** vtable = nullptr;
        SteamClientGetUtils_t getUtils = nullptr;
        SteamClientGetGeneric_t getGeneric = nullptr;
    };

    constexpr size_t kMaxSteamClientVtables = 24;
    SteamClientVtableRecord g_steamClientVtables[kMaxSteamClientVtables]{};
    size_t g_steamClientVtableCount = 0;
    SRWLOCK g_steamClientVtableLock = SRWLOCK_INIT;

    HMODULE g_steamClientModule = nullptr;

    volatile LONG g_steamClientObjectPatched = FALSE;
    volatile LONG g_steamUtils007Patched = FALSE;
    volatile LONG g_steamAppTicketPatched = FALSE;

    // 0 = not scanned, 1 = scan in progress, 2 = all known versions scanned.
    volatile LONG g_steamClientVersionsState = 0;

    // Эти originals вызываются из SteamCompatAsm.asm.
}

extern "C"
{
    // Видны из ASM без C++ name mangling.
    volatile DWORD g_RealAppId = kDefaultRealAppId;
    volatile DWORD g_FakeAppId = kDefaultFakeAppId;

    void* g_OriginalSteamUtils007Slots[10]{};
    void* g_OriginalSteamAppTicketSlot0 = nullptr;

    void HookSteamUtils007Slot0();
    void HookSteamUtils007Slot1();
    void HookSteamUtils007Slot2();
    void HookSteamUtils007Slot3();
    void HookSteamUtils007Slot4();
    void HookSteamUtils007Slot5();
    void HookSteamUtils007Slot6();
    void HookSteamUtils007Slot7();
    void HookSteamUtils007Slot8();
    void HookSteamUtils007Slot9();
    void HookSteamAppTicketSlot0();

    void PatchSteamUtilsR15Field(void* r15Value);
}

namespace
{
    // ============================================================
    // Строки / пути
    // ============================================================

    std::string Trim(const std::string& value)
    {
        const size_t first =
            value.find_first_not_of(" \t\r\n");

        if (first == std::string::npos)
            return {};

        const size_t last =
            value.find_last_not_of(" \t\r\n");

        return value.substr(
            first,
            last - first + 1
        );
    }

    std::wstring DirectoryOfPath(
        const std::wstring& path)
    {
        const size_t pos =
            path.find_last_of(L"\\/");

        if (pos == std::wstring::npos)
            return {};

        return path.substr(0, pos + 1);
    }

    std::wstring ParentDirectory(
        std::wstring directory)
    {
        while (
            !directory.empty() &&
            (directory.back() == L'\\' ||
                directory.back() == L'/')
            ) {
            directory.pop_back();
        }

        return DirectoryOfPath(directory);
    }

    std::wstring GetModulePath(HMODULE module)
    {
        // Common case: avoid a 32K wchar heap allocation for every module.
        wchar_t stackPath[1024]{};

        DWORD length =
            GetModuleFileNameW(
                module,
                stackPath,
                static_cast<DWORD>(std::size(stackPath))
            );

        if (
            length > 0 &&
            length < std::size(stackPath) - 1
            ) {
            return std::wstring(
                stackPath,
                length
            );
        }

        // Long-path fallback only when actually needed.
        std::vector<wchar_t> longPath(32768);

        length =
            GetModuleFileNameW(
                module,
                longPath.data(),
                static_cast<DWORD>(longPath.size())
            );

        if (
            length == 0 ||
            length >= longPath.size()
            ) {
            return {};
        }

        return std::wstring(
            longPath.data(),
            length
        );
    }

    std::wstring GetExecutableDirectory()
    {
        return DirectoryOfPath(
            GetModulePath(nullptr)
        );
    }

    std::wstring GetSelfDirectory()
    {
        return DirectoryOfPath(
            GetModulePath(g_self)
        );
    }

    bool StartsWithInsensitive(
        const std::wstring& text,
        const std::wstring& prefix)
    {
        if (
            prefix.empty() ||
            text.size() < prefix.size()
            ) {
            return false;
        }

        return _wcsnicmp(
            text.c_str(),
            prefix.c_str(),
            prefix.size()
        ) == 0;
    }

    std::wstring BaseNameOfModule(
        HMODULE module)
    {
        const std::wstring full =
            GetModulePath(module);

        if (full.empty())
            return {};

        const size_t pos =
            full.find_last_of(L"\\/");

        if (pos == std::wstring::npos)
            return full;

        return full.substr(pos + 1);
    }

    bool FileExists(
        const std::wstring& path)
    {
        const DWORD attributes =
            GetFileAttributesW(path.c_str());

        return
            attributes != INVALID_FILE_ATTRIBUTES &&
            (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
    }

    // ============================================================
    // appid_config.txt
    //
    // Формат:
    // realid=4001890
    // fakeid=480
    //
    // Ищем:
    // 1) рядом со SteamCompat.dll
    // 2) на папку выше
    // 3) рядом с EXE
    // 4) на папку выше EXE
    //
    // Это покрывает текущую структуру:
    // ...\How to Fish\appid_config.txt
    // ...\How to Fish\How to Fish\How to Fish.exe
    // ============================================================

    bool ReadWholeFile(
        const std::wstring& path,
        std::string& result)
    {
        HANDLE file =
            CreateFileW(
                path.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ |
                FILE_SHARE_WRITE |
                FILE_SHARE_DELETE,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr
            );

        if (file == INVALID_HANDLE_VALUE)
            return false;

        LARGE_INTEGER size{};

        if (
            !GetFileSizeEx(file, &size) ||
            size.QuadPart < 0 ||
            size.QuadPart > 1024 * 1024
            ) {
            CloseHandle(file);
            return false;
        }

        result.resize(
            static_cast<size_t>(
                size.QuadPart
                )
        );

        DWORD read = 0;

        const BOOL ok =
            result.empty()
            ? TRUE
            : ReadFile(
                file,
                &result[0],
                static_cast<DWORD>(
                    result.size()
                    ),
                &read,
                nullptr
            );

        CloseHandle(file);

        if (!ok)
            return false;

        result.resize(read);

        // UTF-8 BOM.
        if (
            result.size() >= 3 &&
            static_cast<unsigned char>(result[0]) == 0xEF &&
            static_cast<unsigned char>(result[1]) == 0xBB &&
            static_cast<unsigned char>(result[2]) == 0xBF
            ) {
            result.erase(0, 3);
        }

        return true;
    }

    bool ParseAppIdConfig(
        const std::string& text,
        DWORD& realId,
        DWORD& fakeId)
    {
        bool haveReal = false;
        bool haveFake = false;

        size_t start = 0;

        while (start <= text.size())
        {
            const size_t end =
                text.find('\n', start);

            std::string line =
                end == std::string::npos
                ? text.substr(start)
                : text.substr(
                    start,
                    end - start
                );

            line = Trim(line);

            if (
                !line.empty() &&
                line[0] != '#' &&
                line[0] != ';'
                ) {
                const size_t eq =
                    line.find('=');

                if (eq != std::string::npos)
                {
                    std::string key =
                        Trim(
                            line.substr(0, eq)
                        );

                    std::string value =
                        Trim(
                            line.substr(eq + 1)
                        );

                    for (char& ch : key)
                        ch = static_cast<char>(
                            tolower(
                                static_cast<unsigned char>(ch)
                            )
                            );

                    char* parseEnd = nullptr;

                    const unsigned long parsed =
                        std::strtoul(
                            value.c_str(),
                            &parseEnd,
                            10
                        );

                    if (
                        parseEnd != value.c_str() &&
                        *parseEnd == '\0' &&
                        parsed > 0 &&
                        parsed <= 0xFFFFFFFFul
                        ) {
                        if (key == "realid")
                        {
                            realId =
                                static_cast<DWORD>(parsed);

                            haveReal = true;
                        }
                        else if (key == "fakeid")
                        {
                            fakeId =
                                static_cast<DWORD>(parsed);

                            haveFake = true;
                        }
                    }
                }
            }

            if (end == std::string::npos)
                break;

            start = end + 1;
        }

        return haveReal && haveFake;
    }

    void LoadAppIdConfig()
    {
        const std::wstring selfDir =
            GetSelfDirectory();

        const std::wstring exeDir =
            GetExecutableDirectory();

        const std::wstring candidates[] =
        {
            selfDir + L"appid_config.txt",
            ParentDirectory(selfDir) +
                L"appid_config.txt",

            exeDir + L"appid_config.txt",
            ParentDirectory(exeDir) +
                L"appid_config.txt"
        };

        for (const auto& path : candidates)
        {
            if (!FileExists(path))
                continue;

            std::string text;

            if (!ReadWholeFile(path, text))
                continue;

            DWORD realId =
                kDefaultRealAppId;

            DWORD fakeId =
                kDefaultFakeAppId;

            if (
                ParseAppIdConfig(
                    text,
                    realId,
                    fakeId
                )
                ) {
                InterlockedExchange(
                    reinterpret_cast<volatile LONG*>(
                        &g_RealAppId
                        ),
                    static_cast<LONG>(realId)
                );

                InterlockedExchange(
                    reinterpret_cast<volatile LONG*>(
                        &g_FakeAppId
                        ),
                    static_cast<LONG>(fakeId)
                );

                _snwprintf_s(
                    g_fakeAppIdW,
                    std::size(g_fakeAppIdW),
                    _TRUNCATE,
                    L"%lu",
                    static_cast<unsigned long>(fakeId)
                );

                _snprintf_s(
                    g_fakeAppIdA,
                    std::size(g_fakeAppIdA),
                    _TRUNCATE,
                    "%lu",
                    static_cast<unsigned long>(fakeId)
                );

                return;
            }
        }
    }

    DWORD RealAppId()
    {
        return static_cast<DWORD>(
            InterlockedCompareExchange(
                reinterpret_cast<volatile LONG*>(
                    &g_RealAppId
                    ),
                0,
                0
            )
            );
    }

    DWORD FakeAppId()
    {
        return static_cast<DWORD>(
            InterlockedCompareExchange(
                reinterpret_cast<volatile LONG*>(
                    &g_FakeAppId
                    ),
                0,
                0
            )
            );
    }

    // ============================================================
    // Environment
    // ============================================================

    void ResolveWindowsApis()
    {
        HMODULE kernel32 =
            GetModuleHandleW(
                L"kernel32.dll"
            );

        if (kernel32 == nullptr)
            return;

        g_realGetProcAddress =
            reinterpret_cast<GetProcAddress_t>(
                ::GetProcAddress(
                    kernel32,
                    "GetProcAddress"
                )
                );

        g_realLoadLibraryW =
            reinterpret_cast<LoadLibraryW_t>(
                ::GetProcAddress(
                    kernel32,
                    "LoadLibraryW"
                )
                );

        g_realLoadLibraryA =
            reinterpret_cast<LoadLibraryA_t>(
                ::GetProcAddress(
                    kernel32,
                    "LoadLibraryA"
                )
                );

        g_realLoadLibraryExW =
            reinterpret_cast<LoadLibraryExW_t>(
                ::GetProcAddress(
                    kernel32,
                    "LoadLibraryExW"
                )
                );

        g_realLoadLibraryExA =
            reinterpret_cast<LoadLibraryExA_t>(
                ::GetProcAddress(
                    kernel32,
                    "LoadLibraryExA"
                )
                );

        g_realSetEnvironmentVariableW =
            reinterpret_cast<
            SetEnvironmentVariableW_t
            >(
                ::GetProcAddress(
                    kernel32,
                    "SetEnvironmentVariableW"
                )
                );

        g_realSetEnvironmentVariableA =
            reinterpret_cast<
            SetEnvironmentVariableA_t
            >(
                ::GetProcAddress(
                    kernel32,
                    "SetEnvironmentVariableA"
                )
                );
    }

    void ForceSteamEnvironment()
    {
        if (
            g_realSetEnvironmentVariableW ==
            nullptr
            ) {
            return;
        }

        g_realSetEnvironmentVariableW(
            L"SteamClientLaunch",
            L"1"
        );

        g_realSetEnvironmentVariableW(
            L"SteamGameId",
            g_fakeAppIdW
        );

        g_realSetEnvironmentVariableW(
            L"SteamAppId",
            g_fakeAppIdW
        );

        g_realSetEnvironmentVariableW(
            L"SteamOverlayGameId",
            g_fakeAppIdW
        );

        g_realSetEnvironmentVariableW(
            L"SteamEnv",
            L"1"
        );
    }

    bool IsSteamIdentityVariableW(
        LPCWSTR name)
    {
        if (name == nullptr)
            return false;

        return
            lstrcmpiW(
                name,
                L"SteamAppId"
            ) == 0 ||
            lstrcmpiW(
                name,
                L"SteamGameId"
            ) == 0;
    }

    bool IsSteamIdentityVariableA(
        LPCSTR name)
    {
        if (name == nullptr)
            return false;

        return
            lstrcmpiA(
                name,
                "SteamAppId"
            ) == 0 ||
            lstrcmpiA(
                name,
                "SteamGameId"
            ) == 0;
    }

    BOOL WINAPI HookSetEnvironmentVariableW(
        LPCWSTR name,
        LPCWSTR value)
    {
        if (
            value != nullptr &&
            IsSteamIdentityVariableW(name)
            ) {
            return
                g_realSetEnvironmentVariableW
                ? g_realSetEnvironmentVariableW(
                    name,
                    g_fakeAppIdW
                )
                : FALSE;
        }

        return
            g_realSetEnvironmentVariableW
            ? g_realSetEnvironmentVariableW(
                name,
                value
            )
            : FALSE;
    }

    BOOL WINAPI HookSetEnvironmentVariableA(
        LPCSTR name,
        LPCSTR value)
    {
        if (
            value != nullptr &&
            IsSteamIdentityVariableA(name)
            ) {
            return
                g_realSetEnvironmentVariableA
                ? g_realSetEnvironmentVariableA(
                    name,
                    g_fakeAppIdA
                )
                : FALSE;
        }

        return
            g_realSetEnvironmentVariableA
            ? g_realSetEnvironmentVariableA(
                name,
                value
            )
            : FALSE;
    }

    // ============================================================
    // Overlay
    // ============================================================

    void EnsureOverlayLoaded()
    {
        if (
            GetModuleHandleW(
                L"gameoverlayrenderer64.dll"
            ) != nullptr
            ) {
            return;
        }

        // Точно как в текущем hook.js.
        if (g_realLoadLibraryW != nullptr)
        {
            g_realLoadLibraryW(
                L"C:\\Program Files (x86)\\Steam\\"
                L"gameoverlayrenderer64.dll"
            );
        }
    }

    // ============================================================
    // Steam API
    // ============================================================

    void ResolveSteamApiOriginals()
    {
        if (g_realGetProcAddress == nullptr)
            return;

        HMODULE steam =
            GetModuleHandleW(
                L"steam_api64.dll"
            );

        if (steam == nullptr)
            return;

        // Fast path after the module has already been resolved.
        if (g_steamApiModule == steam)
            return;

        AcquireSRWLockExclusive(
            &g_resolveLock
        );

        if (g_steamApiModule != steam)
        {
            g_steamApiModule = steam;

            g_realSteamInternalInit =
                reinterpret_cast<
                SteamInternalInit_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamInternal_SteamAPI_Init"
                    )
                    );

            g_realSteamInitFlat =
                reinterpret_cast<
                SteamInitFlat_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamAPI_InitFlat"
                    )
                    );

            g_realSteamInit =
                reinterpret_cast<
                SteamInitBool_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamAPI_Init"
                    )
                    );

            g_realSteamInitSafe =
                reinterpret_cast<
                SteamInitBool_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamAPI_InitSafe"
                    )
                    );

            g_realGetHSteamPipe =
                reinterpret_cast<
                SteamGetHSteamPipe_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamAPI_GetHSteamPipe"
                    )
                    );

            if (g_realGetHSteamPipe == nullptr)
            {
                g_realGetHSteamPipe =
                    reinterpret_cast<
                    SteamGetHSteamPipe_t
                    >(
                        g_realGetProcAddress(
                            steam,
                            "GetHSteamPipe"
                        )
                        );
            }

            g_realGetHSteamUser =
                reinterpret_cast<
                SteamGetHSteamUser_t
                >(
                    g_realGetProcAddress(
                        steam,
                        "SteamAPI_GetHSteamUser"
                    )
                    );

            if (g_realGetHSteamUser == nullptr)
            {
                g_realGetHSteamUser =
                    reinterpret_cast<
                    SteamGetHSteamUser_t
                    >(
                        g_realGetProcAddress(
                            steam,
                            "GetHSteamUser"
                        )
                        );
            }
        }

        ReleaseSRWLockExclusive(
            &g_resolveLock
        );
    }

    bool __cdecl HookSteamRestart(
        std::uint32_t)
    {
        // restartSkipOriginal=true
        // restartForceFalse=true
        return false;
    }

    std::uint32_t __cdecl HookSteamGetAppId(
        void*)
    {
        // Точно как silent GetAppID fast path:
        // игра получает REAL_APP_ID.
        return RealAppId();
    }

    void ResolveSteamClientOriginals();
    void PatchSteamClientVersionsEarly();
    void ProactivelyPatchSteamClientInterfaces();

    int __cdecl HookSteamInternalInit(
        const char* versions,
        void* outError)
    {
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();
        ResolveSteamClientOriginals();

        // До настоящего Steam Init патчим ISteamClient::GetISteamUtils.
        // Поэтому если init получает SteamUtils007 внутри себя,
        // наш HookSteamClientGetUtils увидит объект ДО возврата caller-у.
        ProactivelyPatchSteamClientInterfaces();

        if (g_realSteamInternalInit == nullptr)
            return 1;

        const int result =
            g_realSteamInternalInit(
                versions,
                outError
            );

        // Не выпускаем игру из Steam Init, пока не попробовали
        // установить глобальный hook на РЕАЛЬНЫЙ адрес slot 9.
        ProactivelyPatchSteamClientInterfaces();

        return result;
    }

    int __cdecl HookSteamInitFlat(
        void* outError)
    {
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();
        ResolveSteamClientOriginals();
        ProactivelyPatchSteamClientInterfaces();

        if (g_realSteamInitFlat == nullptr)
            return 1;

        const int result =
            g_realSteamInitFlat(
                outError
            );

        ProactivelyPatchSteamClientInterfaces();

        return result;
    }

    bool __cdecl HookSteamInit()
    {
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();
        ResolveSteamClientOriginals();
        ProactivelyPatchSteamClientInterfaces();

        if (g_realSteamInit == nullptr)
            return false;

        const bool result =
            g_realSteamInit();

        ProactivelyPatchSteamClientInterfaces();

        return result;
    }

    bool __cdecl HookSteamInitSafe()
    {
        ForceSteamEnvironment();
        ResolveSteamApiOriginals();
        ResolveSteamClientOriginals();
        ProactivelyPatchSteamClientInterfaces();

        if (g_realSteamInitSafe == nullptr)
            return false;

        const bool result =
            g_realSteamInitSafe();

        ProactivelyPatchSteamClientInterfaces();

        return result;
    }

    // ============================================================
    // steamclient64.dll
    // ============================================================

    bool WritePointer(
        void** slot,
        void* replacement)
    {
        if (
            slot == nullptr ||
            replacement == nullptr
            ) {
            return false;
        }

        if (*slot == replacement)
            return true;

        DWORD oldProtect = 0;

        if (
            !VirtualProtect(
                slot,
                sizeof(void*),
                PAGE_READWRITE,
                &oldProtect
            )
            ) {
            return false;
        }

        InterlockedExchangePointer(
            slot,
            replacement
        );

        DWORD ignored = 0;

        VirtualProtect(
            slot,
            sizeof(void*),
            oldProtect,
            &ignored
        );

        FlushInstructionCache(
            GetCurrentProcess(),
            slot,
            sizeof(void*)
        );

        return true;
    }

    // ============================================================
    // SteamUtils007 vtable hooks.
    //
    // v5 deliberately hooks slots 0..9 synchronously before the
    // SteamUtils007 object is returned to its caller.
    //
    // This removes the old global inline-hook/lock machinery.
    // ============================================================

    void PatchSteamUtils007Object(
        void* objectPtr)
    {
        if (objectPtr == nullptr)
            return;

        if (
            InterlockedCompareExchange(
                &g_steamUtils007Patched,
                0,
                0
            ) != FALSE
            ) {
            return;
        }

        __try
        {
            auto** vtable =
                *reinterpret_cast<void***>(
                    objectPtr
                    );

            if (vtable == nullptr)
                return;

            // slot 9 exists, therefore slots 0..9 are valid for SteamUtils007.
            // We hook ALL of them so we do not depend on "slot 2 happens first".
            //
            // Each ASM wrapper first checks:
            //     if ([R15+0x38] == realid) -> fakeid
            // and then passes the call to the exact original method.
            //
            // slot 2 and slot 9 additionally repeat the check AFTER original
            // returns, because those two are the calls we have actually observed.
            void* const hooks[10] =
            {
                reinterpret_cast<void*>(&HookSteamUtils007Slot0),
                reinterpret_cast<void*>(&HookSteamUtils007Slot1),
                reinterpret_cast<void*>(&HookSteamUtils007Slot2),
                reinterpret_cast<void*>(&HookSteamUtils007Slot3),
                reinterpret_cast<void*>(&HookSteamUtils007Slot4),
                reinterpret_cast<void*>(&HookSteamUtils007Slot5),
                reinterpret_cast<void*>(&HookSteamUtils007Slot6),
                reinterpret_cast<void*>(&HookSteamUtils007Slot7),
                reinterpret_cast<void*>(&HookSteamUtils007Slot8),
                reinterpret_cast<void*>(&HookSteamUtils007Slot9)
            };

            bool allReady = true;

            for (size_t i = 0; i < 10; ++i)
            {
                void** slot =
                    &vtable[i];

                void* current =
                    *slot;

                if (current == hooks[i])
                    continue;

                if (
                    g_OriginalSteamUtils007Slots[i] ==
                    nullptr
                    ) {
                    g_OriginalSteamUtils007Slots[i] =
                        current;
                }
                else if (
                    g_OriginalSteamUtils007Slots[i] !=
                    current
                    ) {
                    // Another SteamUtils007 implementation/version.
                    // Do not chain to an unknown original.
                    allReady = false;
                    continue;
                }

                if (
                    !WritePointer(
                        slot,
                        hooks[i]
                    )
                    ) {
                    allReady = false;
                }
            }

            if (allReady)
            {
                InterlockedExchange(
                    &g_steamUtils007Patched,
                    TRUE
                );
            }
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
        }
    }

    void PatchSteamAppTicketObject(
        void* objectPtr)
    {
        if (objectPtr == nullptr)
            return;

        if (
            InterlockedCompareExchange(
                &g_steamAppTicketPatched,
                0,
                0
            ) != FALSE
            ) {
            return;
        }

        __try
        {
            auto** vtable =
                *reinterpret_cast<void***>(
                    objectPtr
                    );

            if (vtable == nullptr)
                return;

            void** slot = &vtable[0];
            void* current = *slot;

            if (
                current ==
                reinterpret_cast<void*>(
                    &HookSteamAppTicketSlot0
                    )
                ) {
                InterlockedExchange(
                    &g_steamAppTicketPatched,
                    TRUE
                );
                return;
            }

            if (
                g_OriginalSteamAppTicketSlot0 ==
                nullptr
                ) {
                g_OriginalSteamAppTicketSlot0 =
                    current;
            }
            else if (
                g_OriginalSteamAppTicketSlot0 !=
                current
                ) {
                return;
            }

            if (
                WritePointer(
                    slot,
                    reinterpret_cast<void*>(
                        &HookSteamAppTicketSlot0
                        )
                )
                ) {
                InterlockedExchange(
                    &g_steamAppTicketPatched,
                    TRUE
                );
            }
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
        }
    }

    SteamClientVtableRecord FindSteamClientVtableRecord(
        void* self)
    {
        SteamClientVtableRecord result{};

        if (self == nullptr)
            return result;

        void** vtable = nullptr;

        __try
        {
            vtable =
                *reinterpret_cast<void***>(
                    self
                    );
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
            vtable = nullptr;
        }

        if (vtable == nullptr)
            return result;

        AcquireSRWLockShared(
            &g_steamClientVtableLock
        );

        for (
            size_t i = 0;
            i < g_steamClientVtableCount;
            ++i
            ) {
            if (
                g_steamClientVtables[i].vtable ==
                vtable
                ) {
                result =
                    g_steamClientVtables[i];

                break;
            }
        }

        ReleaseSRWLockShared(
            &g_steamClientVtableLock
        );

        return result;
    }

    void RememberSteamClientVtable(
        void* objectPtr,
        void** vtable,
        SteamClientGetUtils_t getUtils,
        SteamClientGetGeneric_t getGeneric)
    {
        if (vtable == nullptr)
            return;

        AcquireSRWLockExclusive(
            &g_steamClientVtableLock
        );

        for (
            size_t i = 0;
            i < g_steamClientVtableCount;
            ++i
            ) {
            if (
                g_steamClientVtables[i].vtable ==
                vtable
                ) {
                if (
                    g_steamClientVtables[i].object == nullptr &&
                    objectPtr != nullptr
                    ) {
                    g_steamClientVtables[i].object = objectPtr;
                }

                if (
                    g_steamClientVtables[i]
                    .getUtils == nullptr &&
                    getUtils != nullptr
                    ) {
                    g_steamClientVtables[i]
                        .getUtils = getUtils;
                }

                if (
                    g_steamClientVtables[i]
                    .getGeneric == nullptr &&
                    getGeneric != nullptr
                    ) {
                    g_steamClientVtables[i]
                        .getGeneric = getGeneric;
                }

                ReleaseSRWLockExclusive(
                    &g_steamClientVtableLock
                );

                return;
            }
        }

        if (
            g_steamClientVtableCount <
            kMaxSteamClientVtables
            ) {
            auto& record =
                g_steamClientVtables[
                    g_steamClientVtableCount++
                ];

            record.object = objectPtr;
            record.vtable = vtable;
            record.getUtils = getUtils;
            record.getGeneric = getGeneric;
        }

        ReleaseSRWLockExclusive(
            &g_steamClientVtableLock
        );
    }

    void PatchCurrentThreadR15IfNeeded()
    {
#ifdef _WIN64
        CONTEXT context{};
        RtlCaptureContext(
            &context
        );

        PatchSteamUtilsR15Field(
            reinterpret_cast<void*>(
                context.R15
                )
        );
#endif
    }

    void* __fastcall HookSteamClientGetUtils(
        void* self,
        int hSteamPipe,
        const char* version)
    {
        // Earliest guarded opportunity we have:
        // this happens before SteamUtils007 is returned to the game.
        PatchCurrentThreadR15IfNeeded();

        const SteamClientVtableRecord record =
            FindSteamClientVtableRecord(
                self
            );

        SteamClientGetUtils_t original =
            record.getUtils;

        if (original == nullptr)
            original =
            g_realSteamClientGetUtils;

        if (original == nullptr)
            return nullptr;

        void* result =
            original(
                self,
                hSteamPipe,
                version
            );

        if (
            result != nullptr &&
            version != nullptr &&
            std::strcmp(
                version,
                "SteamUtils007"
            ) == 0
            ) {
            // Критически важно:
            // все SteamUtils007 slots 0..9 патчатся СИНХРОННО,
            // до того как указатель SteamUtils007 возвращается caller-у.
            PatchSteamUtils007Object(
                result
            );
        }

        return result;
    }

    void* __fastcall HookSteamClientGetGeneric(
        void* self,
        int hSteamUser,
        int hSteamPipe,
        const char* version)
    {
        PatchCurrentThreadR15IfNeeded();

        const SteamClientVtableRecord record =
            FindSteamClientVtableRecord(
                self
            );

        SteamClientGetGeneric_t original =
            record.getGeneric;

        if (original == nullptr)
            original =
            g_realSteamClientGetGeneric;

        if (original == nullptr)
            return nullptr;

        void* result =
            original(
                self,
                hSteamUser,
                hSteamPipe,
                version
            );

        if (
            result != nullptr &&
            version != nullptr &&
            std::strcmp(
                version,
                "STEAMAPPTICKET_INTERFACE_VERSION001"
            ) == 0
            ) {
            PatchSteamAppTicketObject(
                result
            );
        }

        return result;
    }

    void PatchSteamClientObject(
        void* objectPtr)
    {
        if (objectPtr == nullptr)
            return;

        __try
        {
            auto** vtable =
                *reinterpret_cast<void***>(
                    objectPtr
                    );

            if (vtable == nullptr)
                return;

            void* currentUtils =
                vtable[9];

            void* currentGeneric =
                vtable[12];

            SteamClientGetUtils_t originalUtils =
                nullptr;

            SteamClientGetGeneric_t originalGeneric =
                nullptr;

            if (
                currentUtils ==
                reinterpret_cast<void*>(
                    &HookSteamClientGetUtils
                    )
                ) {
                const auto existing =
                    FindSteamClientVtableRecord(
                        objectPtr
                    );

                originalUtils =
                    existing.getUtils;
            }
            else
            {
                originalUtils =
                    reinterpret_cast<
                    SteamClientGetUtils_t
                    >(currentUtils);
            }

            if (
                currentGeneric ==
                reinterpret_cast<void*>(
                    &HookSteamClientGetGeneric
                    )
                ) {
                const auto existing =
                    FindSteamClientVtableRecord(
                        objectPtr
                    );

                originalGeneric =
                    existing.getGeneric;
            }
            else
            {
                originalGeneric =
                    reinterpret_cast<
                    SteamClientGetGeneric_t
                    >(currentGeneric);
            }

            RememberSteamClientVtable(
                objectPtr,
                vtable,
                originalUtils,
                originalGeneric
            );

            // Эти globals оставляем только как fallback.
            if (
                g_realSteamClientGetUtils ==
                nullptr &&
                originalUtils != nullptr
                ) {
                g_realSteamClientGetUtils =
                    originalUtils;
            }

            if (
                g_realSteamClientGetGeneric ==
                nullptr &&
                originalGeneric != nullptr
                ) {
                g_realSteamClientGetGeneric =
                    originalGeneric;
            }

            bool utilsReady = false;

            if (
                currentUtils ==
                reinterpret_cast<void*>(
                    &HookSteamClientGetUtils
                    )
                ) {
                utilsReady = true;
            }
            else
            {
                utilsReady =
                    WritePointer(
                        &vtable[9],
                        reinterpret_cast<void*>(
                            &HookSteamClientGetUtils
                            )
                    );
            }

            if (
                currentGeneric !=
                reinterpret_cast<void*>(
                    &HookSteamClientGetGeneric
                    )
                ) {
                WritePointer(
                    &vtable[12],
                    reinterpret_cast<void*>(
                        &HookSteamClientGetGeneric
                        )
                );
            }

            if (utilsReady)
            {
                InterlockedExchange(
                    &g_steamClientObjectPatched,
                    TRUE
                );
            }
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
        }
    }

    void ResolveSteamClientOriginals()
    {
        if (g_realGetProcAddress == nullptr)
            return;

        HMODULE client =
            GetModuleHandleW(
                L"steamclient64.dll"
            );

        if (client == nullptr)
            return;

        // Fast path: this function is called very often during startup.
        if (g_steamClientModule == client)
            return;

        AcquireSRWLockExclusive(
            &g_resolveLock
        );

        if (g_steamClientModule != client)
        {
            g_steamClientModule = client;

            // A reloaded steamclient needs a fresh version pass.
            InterlockedExchange(
                &g_steamClientVersionsState,
                0
            );

            g_realCreateInterface =
                reinterpret_cast<
                CreateInterface_t
                >(
                    g_realGetProcAddress(
                        client,
                        "CreateInterface"
                    )
                    );
        }

        ReleaseSRWLockExclusive(
            &g_resolveLock
        );
    }

    // ============================================================
    // Активное восстановление SteamClient interfaces.
    //
    // Почему это нужно:
    // Frida ставит Interceptor непосредственно на steamclient64!CreateInterface
    // и успевает увидеть уже самые ранние вызовы.
    //
    // DLL, загруженная через Proxy, может начать работу ПОСЛЕ того,
    // как SteamClient object / SteamUtils007 object уже были получены.
    // Тогда один только IAT/GetProcAddress hook уже ничего не увидит.
    //
    // Поэтому мы сами получаем существующий SteamClient interface,
    // берём текущие HSteamPipe/HSteamUser и принудительно запрашиваем:
    //   SteamUtils007
    //   STEAMAPPTICKET_INTERFACE_VERSION001
    //
    // После этого нужные vtable slots гарантированно известны и патчатся.
    // ============================================================

    void PatchSteamClientVersionsEarly()
    {
        // This pass is independent of steam_api64.dll and only needs
        // steamclient64.dll. Each steamclient module is scanned once.
        ResolveSteamClientOriginals();

        if (g_realCreateInterface == nullptr)
            return;

        const LONG previousState =
            InterlockedCompareExchange(
                &g_steamClientVersionsState,
                1,
                0
            );

        if (previousState != 0)
            return;

        static const char* const kClientVersions[] =
        {
            "SteamClient023",
            "SteamClient022",
            "SteamClient021",
            "SteamClient020",
            "SteamClient019",
            "SteamClient018",
            "SteamClient017",
            "SteamClient016",
            "SteamClient015",
            "SteamClient014"
        };

        for (const char* version : kClientVersions)
        {
            void* clientObject = nullptr;

            __try
            {
                int returnCode = 0;

                clientObject =
                    g_realCreateInterface(
                        version,
                        &returnCode
                    );
            }
            __except (
                EXCEPTION_EXECUTE_HANDLER
                ) {
                clientObject = nullptr;
            }

            if (clientObject != nullptr)
            {
                // Для каждой версии сохраняется СВОЙ original slot 9/12.
                // Это исправляет ошибку v3, где один global original
                // мог принадлежать другой версии SteamClient.
                PatchSteamClientObject(
                    clientObject
                );
            }
        }

        InterlockedExchange(
            &g_steamClientVersionsState,
            InterlockedCompareExchange(
                &g_steamClientObjectPatched,
                0,
                0
            ) != FALSE
            ? 2
            : 0
        );
    }

    void ProactivelyPatchSteamClientInterfaces()
    {
        // The expensive CreateInterface(version...) discovery is cached:
        // PatchSteamClientVersionsEarly() executes it only once per module.
        PatchSteamClientVersionsEarly();

        if (
            InterlockedCompareExchange(
                &g_steamUtils007Patched,
                0,
                0
            ) != FALSE &&
            InterlockedCompareExchange(
                &g_steamAppTicketPatched,
                0,
                0
            ) != FALSE
            ) {
            return;
        }

        ResolveSteamApiOriginals();

        int hSteamPipe = 0;
        int hSteamUser = 0;

        __try
        {
            if (g_realGetHSteamPipe != nullptr)
                hSteamPipe = g_realGetHSteamPipe();

            if (g_realGetHSteamUser != nullptr)
                hSteamUser = g_realGetHSteamUser();
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
            hSteamPipe = 0;
            hSteamUser = 0;
        }

        if (hSteamPipe == 0)
            return;

        // Reuse objects/interfaces discovered by the one-time version scan.
        // This avoids repeatedly calling CreateInterface for 10 versions.
        SteamClientVtableRecord records[
            kMaxSteamClientVtables
        ]{};

            size_t recordCount = 0;

            AcquireSRWLockShared(
                &g_steamClientVtableLock
            );

            recordCount =
                (g_steamClientVtableCount <
                    kMaxSteamClientVtables)
                ? g_steamClientVtableCount
                : kMaxSteamClientVtables;

            for (size_t i = 0; i < recordCount; ++i)
                records[i] = g_steamClientVtables[i];

            ReleaseSRWLockShared(
                &g_steamClientVtableLock
            );

            for (size_t i = 0; i < recordCount; ++i)
            {
                const SteamClientVtableRecord& record =
                    records[i];

                void* clientObject = record.object;

                if (clientObject == nullptr)
                    continue;

                if (
                    InterlockedCompareExchange(
                        &g_steamUtils007Patched,
                        0,
                        0
                    ) == FALSE &&
                    record.getUtils != nullptr
                    ) {
                    void* utils007 = nullptr;

                    __try
                    {
                        utils007 =
                            record.getUtils(
                                clientObject,
                                hSteamPipe,
                                "SteamUtils007"
                            );
                    }
                    __except (
                        EXCEPTION_EXECUTE_HANDLER
                        ) {
                        utils007 = nullptr;
                    }

                    if (utils007 != nullptr)
                        PatchSteamUtils007Object(utils007);
                }

                if (
                    InterlockedCompareExchange(
                        &g_steamAppTicketPatched,
                        0,
                        0
                    ) == FALSE &&
                    hSteamUser != 0 &&
                    record.getGeneric != nullptr
                    ) {
                    void* appTicket = nullptr;

                    __try
                    {
                        appTicket =
                            record.getGeneric(
                                clientObject,
                                hSteamUser,
                                hSteamPipe,
                                "STEAMAPPTICKET_INTERFACE_VERSION001"
                            );
                    }
                    __except (
                        EXCEPTION_EXECUTE_HANDLER
                        ) {
                        appTicket = nullptr;
                    }

                    if (appTicket != nullptr)
                        PatchSteamAppTicketObject(appTicket);
                }

                if (
                    InterlockedCompareExchange(
                        &g_steamUtils007Patched,
                        0,
                        0
                    ) != FALSE &&
                    InterlockedCompareExchange(
                        &g_steamAppTicketPatched,
                        0,
                        0
                    ) != FALSE
                    ) {
                    break;
                }
            }
    }

    void* __cdecl HookCreateInterface(
        const char* name,
        int* returnCode)
    {
        ResolveSteamClientOriginals();

        if (g_realCreateInterface == nullptr)
            return nullptr;

        void* result =
            g_realCreateInterface(
                name,
                returnCode
            );

        if (
            result != nullptr &&
            name != nullptr &&
            _strnicmp(
                name,
                "SteamClient",
                11
            ) == 0
            ) {
            PatchSteamClientObject(
                result
            );
        }

        return result;
    }

    // ============================================================
    // Точная native-версия Frida:
    // SteamUtils007 slot 9 onLeave:
    // [R15 + 0x38] REAL -> FAKE
    // retval НЕ меняем.
    //
    // R15 является nonvolatile в Windows x64 ABI.
    // ASM-wrapper сохраняет return, передаёт R15 сюда,
    // потом возвращает исходный RAX.
    // ============================================================

} // namespace

extern "C"
void PatchSteamUtilsR15Field(
    void* r15Value)
{
    if (r15Value == nullptr)
        return;

    __try
    {
        auto* field =
            reinterpret_cast<
            volatile LONG*
            >(
                static_cast<unsigned char*>(
                    r15Value
                    ) + 0x38
                );

        const LONG realId =
            static_cast<LONG>(
                RealAppId()
                );

        const LONG fakeId =
            static_cast<LONG>(
                FakeAppId()
                );

        // Ровно поведение Frida onLeave:
        // меняем только если в этот момент поле равно REAL_APP_ID.
        const LONG previous =
            InterlockedCompareExchange(
                field,
                fakeId,
                realId
            );

        (void)previous;
    }
    __except (
        EXCEPTION_EXECUTE_HANDLER
        ) {
        // Last fallback for a read-only page.
        __try
        {
            auto* field =
                reinterpret_cast<DWORD*>(
                    static_cast<unsigned char*>(
                        r15Value
                        ) + 0x38
                    );

            if (*field != RealAppId())
                return;

            DWORD oldProtect = 0;

            if (
                VirtualProtect(
                    field,
                    sizeof(DWORD),
                    PAGE_READWRITE,
                    &oldProtect
                )
                ) {
                *field = FakeAppId();

                DWORD ignored = 0;

                VirtualProtect(
                    field,
                    sizeof(DWORD),
                    oldProtect,
                    &ignored
                );
            }
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
        }
    }
}

namespace
{
    // ============================================================
    // GetProcAddress / LoadLibrary
    // ============================================================

    bool IsNamedModule(
        HMODULE module,
        const wchar_t* expected)
    {
        if (
            module == nullptr ||
            expected == nullptr
            ) {
            return false;
        }

        const std::wstring name =
            BaseNameOfModule(module);

        return
            !name.empty() &&
            _wcsicmp(
                name.c_str(),
                expected
            ) == 0;
    }

    FARPROC WINAPI HookGetProcAddress(
        HMODULE module,
        LPCSTR procName)
    {
        if (
            g_realGetProcAddress == nullptr ||
            procName == nullptr
            ) {
            return nullptr;
        }

        FARPROC original =
            g_realGetProcAddress(
                module,
                procName
            );

        // Ordinal.
        if (
            (
                reinterpret_cast<ULONG_PTR>(
                    procName
                    ) >> 16
                ) == 0
            ) {
            return original;
        }

        if (
            IsNamedModule(
                module,
                L"steam_api64.dll"
            )
            ) {
            if (
                std::strcmp(
                    procName,
                    "SteamAPI_RestartAppIfNecessary"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamRestart
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SteamAPI_ISteamUtils_GetAppID"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamGetAppId
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SteamInternal_SteamAPI_Init"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamInternalInit
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SteamAPI_InitFlat"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamInitFlat
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SteamAPI_Init"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamInit
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SteamAPI_InitSafe"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSteamInitSafe
                        );
            }
        }

        if (
            IsNamedModule(
                module,
                L"steamclient64.dll"
            ) &&
            std::strcmp(
                procName,
                "CreateInterface"
            ) == 0
            ) {
            if (original != nullptr)
            {
                g_realCreateInterface =
                    reinterpret_cast<
                    CreateInterface_t
                    >(original);
            }

            return
                reinterpret_cast<FARPROC>(
                    &HookCreateInterface
                    );
        }

        if (
            IsNamedModule(
                module,
                L"kernel32.dll"
            ) ||
            IsNamedModule(
                module,
                L"kernelbase.dll"
            )
            ) {
            if (
                std::strcmp(
                    procName,
                    "SetEnvironmentVariableW"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSetEnvironmentVariableW
                        );
            }

            if (
                std::strcmp(
                    procName,
                    "SetEnvironmentVariableA"
                ) == 0
                ) {
                return
                    reinterpret_cast<FARPROC>(
                        &HookSetEnvironmentVariableA
                        );
            }
        }

        return original;
    }

    void PatchModuleImports(
        HMODULE module);

    void PatchGameModules();

    void AfterLibraryLoad(
        HMODULE module)
    {
        if (module == nullptr)
            return;

        ResolveSteamApiOriginals();
        ResolveSteamClientOriginals();

        if (
            InterlockedCompareExchange(
                &g_steamUtils007Patched,
                0,
                0
            ) == FALSE ||
            InterlockedCompareExchange(
                &g_steamAppTicketPatched,
                0,
                0
            ) == FALSE
            ) {
            ProactivelyPatchSteamClientInterfaces();
        }

        const std::wstring path =
            GetModulePath(module);

        if (
            !path.empty() &&
            StartsWithInsensitive(
                path,
                g_gameDirectory
            )
            ) {
            AcquireSRWLockExclusive(
                &g_patchLock
            );

            PatchModuleImports(module);

            ReleaseSRWLockExclusive(
                &g_patchLock
            );
        }
    }

    HMODULE WINAPI HookLoadLibraryW(
        LPCWSTR fileName)
    {
        if (g_realLoadLibraryW == nullptr)
            return nullptr;

        HMODULE module =
            g_realLoadLibraryW(fileName);

        AfterLibraryLoad(module);

        return module;
    }

    HMODULE WINAPI HookLoadLibraryA(
        LPCSTR fileName)
    {
        if (g_realLoadLibraryA == nullptr)
            return nullptr;

        HMODULE module =
            g_realLoadLibraryA(fileName);

        AfterLibraryLoad(module);

        return module;
    }

    HMODULE WINAPI HookLoadLibraryExW(
        LPCWSTR fileName,
        HANDLE file,
        DWORD flags)
    {
        if (g_realLoadLibraryExW == nullptr)
            return nullptr;

        HMODULE module =
            g_realLoadLibraryExW(
                fileName,
                file,
                flags
            );

        AfterLibraryLoad(module);

        return module;
    }

    HMODULE WINAPI HookLoadLibraryExA(
        LPCSTR fileName,
        HANDLE file,
        DWORD flags)
    {
        if (g_realLoadLibraryExA == nullptr)
            return nullptr;

        HMODULE module =
            g_realLoadLibraryExA(
                fileName,
                file,
                flags
            );

        AfterLibraryLoad(module);

        return module;
    }

    // ============================================================
    // IAT patching
    // ============================================================

    bool EqualsImportDll(
        const char* importedDll,
        const char* expected)
    {
        return
            importedDll != nullptr &&
            expected != nullptr &&
            _stricmp(
                importedDll,
                expected
            ) == 0;
    }

    void* ReplacementForImport(
        const char* importedDll,
        const char* functionName)
    {
        if (
            importedDll == nullptr ||
            functionName == nullptr
            ) {
            return nullptr;
        }

        if (
            EqualsImportDll(
                importedDll,
                "kernel32.dll"
            ) ||
            EqualsImportDll(
                importedDll,
                "kernelbase.dll"
            )
            ) {
            if (
                std::strcmp(
                    functionName,
                    "SetEnvironmentVariableW"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSetEnvironmentVariableW
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SetEnvironmentVariableA"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSetEnvironmentVariableA
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "GetProcAddress"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookGetProcAddress
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "LoadLibraryW"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookLoadLibraryW
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "LoadLibraryA"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookLoadLibraryA
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "LoadLibraryExW"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookLoadLibraryExW
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "LoadLibraryExA"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookLoadLibraryExA
                        );
            }
        }

        if (
            EqualsImportDll(
                importedDll,
                "steam_api64.dll"
            )
            ) {
            if (
                std::strcmp(
                    functionName,
                    "SteamAPI_RestartAppIfNecessary"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamRestart
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SteamAPI_ISteamUtils_GetAppID"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamGetAppId
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SteamInternal_SteamAPI_Init"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamInternalInit
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SteamAPI_InitFlat"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamInitFlat
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SteamAPI_Init"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamInit
                        );
            }

            if (
                std::strcmp(
                    functionName,
                    "SteamAPI_InitSafe"
                ) == 0
                ) {
                return
                    reinterpret_cast<void*>(
                        &HookSteamInitSafe
                        );
            }
        }

        if (
            EqualsImportDll(
                importedDll,
                "steamclient64.dll"
            ) &&
            std::strcmp(
                functionName,
                "CreateInterface"
            ) == 0
            ) {
            return
                reinterpret_cast<void*>(
                    &HookCreateInterface
                    );
        }

        return nullptr;
    }

    bool WriteIatPointer(
        ULONG_PTR* slot,
        void* replacement)
    {
        if (
            slot == nullptr ||
            replacement == nullptr
            ) {
            return false;
        }

        if (
            *slot ==
            reinterpret_cast<ULONG_PTR>(
                replacement
                )
            ) {
            return true;
        }

        DWORD oldProtect = 0;

        if (
            !VirtualProtect(
                slot,
                sizeof(void*),
                PAGE_READWRITE,
                &oldProtect
            )
            ) {
            return false;
        }

        InterlockedExchangePointer(
            reinterpret_cast<
            PVOID volatile*
            >(slot),
            replacement
        );

        DWORD ignored = 0;

        VirtualProtect(
            slot,
            sizeof(void*),
            oldProtect,
            &ignored
        );

        FlushInstructionCache(
            GetCurrentProcess(),
            slot,
            sizeof(void*)
        );

        return true;
    }

    void PatchModuleImports(
        HMODULE module)
    {
        if (
            module == nullptr ||
            module == g_self
            ) {
            return;
        }

        __try
        {
            auto* base =
                reinterpret_cast<
                std::uint8_t*
                >(module);

            auto* dos =
                reinterpret_cast<
                IMAGE_DOS_HEADER*
                >(base);

            if (
                dos->e_magic !=
                IMAGE_DOS_SIGNATURE
                ) {
                return;
            }

            auto* nt =
                reinterpret_cast<
                IMAGE_NT_HEADERS*
                >(
                    base +
                    dos->e_lfanew
                    );

            if (
                nt->Signature !=
                IMAGE_NT_SIGNATURE
                ) {
                return;
            }

            const auto& directory =
                nt->OptionalHeader
                .DataDirectory[
                    IMAGE_DIRECTORY_ENTRY_IMPORT
                ];

            if (
                directory.VirtualAddress == 0 ||
                directory.Size == 0
                ) {
                return;
            }

            auto* descriptor =
                reinterpret_cast<
                IMAGE_IMPORT_DESCRIPTOR*
                >(
                    base +
                    directory.VirtualAddress
                    );

            for (
                ;
                descriptor->Name != 0;
                ++descriptor
                ) {
                if (
                    descriptor->FirstThunk == 0 ||
                    descriptor->OriginalFirstThunk == 0
                    ) {
                    continue;
                }

                const char* importedDll =
                    reinterpret_cast<
                    const char*
                    >(
                        base +
                        descriptor->Name
                        );

                auto* originalThunk =
                    reinterpret_cast<
                    IMAGE_THUNK_DATA*
                    >(
                        base +
                        descriptor
                        ->OriginalFirstThunk
                        );

                auto* firstThunk =
                    reinterpret_cast<
                    IMAGE_THUNK_DATA*
                    >(
                        base +
                        descriptor
                        ->FirstThunk
                        );

                for (
                    ;
                    originalThunk
                    ->u1.AddressOfData != 0;
                    ++originalThunk,
                    ++firstThunk
                    ) {
#ifdef _WIN64
                    if (
                        IMAGE_SNAP_BY_ORDINAL64(
                            originalThunk->u1.Ordinal
                        )
                        ) {
                        continue;
                    }
#else
                    if (
                        IMAGE_SNAP_BY_ORDINAL32(
                            originalThunk->u1.Ordinal
                        )
                        ) {
                        continue;
                    }
#endif

                    auto* byName =
                        reinterpret_cast<
                        IMAGE_IMPORT_BY_NAME*
                        >(
                            base +
                            originalThunk
                            ->u1.AddressOfData
                            );

                    const char* functionName =
                        reinterpret_cast<
                        const char*
                        >(
                            byName->Name
                            );

                    void* replacement =
                        ReplacementForImport(
                            importedDll,
                            functionName
                        );

                    if (replacement == nullptr)
                        continue;

                    WriteIatPointer(
                        reinterpret_cast<
                        ULONG_PTR*
                        >(
                            &firstThunk
                            ->u1.Function
                            ),
                        replacement
                    );
                }
            }
        }
        __except (
            EXCEPTION_EXECUTE_HANDLER
            ) {
        }
    }

    bool ShouldPatchModule(
        const MODULEENTRY32W& entry)
    {
        if (
            entry.hModule == nullptr ||
            entry.hModule == g_self
            ) {
            return false;
        }

        return
            StartsWithInsensitive(
                entry.szExePath,
                g_gameDirectory
            );
    }

    void PatchGameModules()
    {
        HANDLE snapshot =
            CreateToolhelp32Snapshot(
                TH32CS_SNAPMODULE |
                TH32CS_SNAPMODULE32,
                GetCurrentProcessId()
            );

        if (
            snapshot ==
            INVALID_HANDLE_VALUE
            ) {
            return;
        }

        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);

        if (
            Module32FirstW(
                snapshot,
                &entry
            )
            ) {
            do
            {
                if (ShouldPatchModule(entry))
                    PatchModuleImports(
                        entry.hModule
                    );
            } while (
                Module32NextW(
                    snapshot,
                    &entry
                )
                );
        }

        CloseHandle(snapshot);
    }

    // ============================================================
    // Короткий fallback scanner.
    //
    // Нужен на случай DLL, загруженных необычным путём.
    // После 15 секунд полностью завершается.
    // ============================================================

    DWORD WINAPI ScannerThread(
        LPVOID)
    {
        // --------------------------------------------------------
        // ФАЗА 1 — самое важное:
        // ждём ТОЛЬКО steamclient64.dll.
        //
        // Никакого ожидания steam_api64.dll здесь нет.
        // Как только steamclient появился, максимально быстро
        // получаем SteamClient interfaces и патчим их vtable slot 9/12.
        // --------------------------------------------------------

        const ULONGLONG earlyDeadline =
            GetTickCount64() + 15000;

        while (
            InterlockedCompareExchange(
                &g_stop,
                0,
                0
            ) == FALSE &&
            GetTickCount64() < earlyDeadline
            ) {
            if (
                GetModuleHandleW(
                    L"steamclient64.dll"
                ) != nullptr
                ) {
                PatchSteamClientVersionsEarly();

                if (
                    InterlockedCompareExchange(
                        &g_steamClientObjectPatched,
                        0,
                        0
                    ) != FALSE
                    ) {
                    // После этого будущий
                    // GetISteamUtils(...,"SteamUtils007")
                    // синхронно патчит slots 0..9
                    // ДО возврата объекта caller-у.
                    break;
                }
            }

            Sleep(1);
        }

        // --------------------------------------------------------
        // PHASE 2 — low-overhead fallback.
        //
        // v5 did a complete Toolhelp module snapshot every 25 ms for
        // 15 seconds (up to 600 full-process scans). That was useful
        // while debugging, but is excessive now that LoadLibrary hooks
        // patch newly loaded game modules synchronously.
        //
        // v6:
        // - keeps compatibility checks for the same ~15 second window;
        // - performs a full module snapshot only every 500 ms;
        // - sleeps longer after SteamUtils007 is already patched;
        // - exits immediately when all required hooks are ready.
        // --------------------------------------------------------

        SetThreadPriority(
            GetCurrentThread(),
            THREAD_PRIORITY_NORMAL
        );

        const ULONGLONG fallbackDeadline =
            GetTickCount64() + 15000;

        ULONGLONG nextFullModuleScan = 0;

        while (
            InterlockedCompareExchange(
                &g_stop,
                0,
                0
            ) == FALSE &&
            GetTickCount64() < fallbackDeadline
            ) {
            ResolveSteamApiOriginals();
            ResolveSteamClientOriginals();

            const bool utilsReady =
                InterlockedCompareExchange(
                    &g_steamUtils007Patched,
                    0,
                    0
                ) != FALSE;

            const bool ticketReady =
                InterlockedCompareExchange(
                    &g_steamAppTicketPatched,
                    0,
                    0
                ) != FALSE;

            if (!utilsReady || !ticketReady)
                ProactivelyPatchSteamClientInterfaces();

            const ULONGLONG now =
                GetTickCount64();

            if (now >= nextFullModuleScan)
            {
                AcquireSRWLockExclusive(
                    &g_patchLock
                );

                PatchGameModules();

                ReleaseSRWLockExclusive(
                    &g_patchLock
                );

                nextFullModuleScan =
                    now + 500;
            }

            const bool steamApiReady =
                g_steamApiModule != nullptr;

            const bool finalUtilsReady =
                InterlockedCompareExchange(
                    &g_steamUtils007Patched,
                    0,
                    0
                ) != FALSE;

            const bool finalTicketReady =
                InterlockedCompareExchange(
                    &g_steamAppTicketPatched,
                    0,
                    0
                ) != FALSE;

            if (
                steamApiReady &&
                finalUtilsReady &&
                finalTicketReady
                ) {
                break;
            }

            Sleep(
                finalUtilsReady
                ? 250
                : 25
            );
        }

        return 0;
    }

    // ============================================================
    // Init
    // ============================================================

    BOOL CALLBACK InitializeOnce(
        PINIT_ONCE,
        PVOID,
        PVOID*)
    {
        ResolveWindowsApis();

        g_gameDirectory =
            GetExecutableDirectory();

        if (!g_gameDirectory.empty())
        {
            SetCurrentDirectoryW(
                g_gameDirectory.c_str()
            );
        }

        LoadAppIdConfig();
        ForceSteamEnvironment();

        // Сканер запускаем КАК МОЖНО РАНЬШЕ.
        // Его первая фаза вообще не смотрит на steam_api64.dll:
        // она ждёт только steamclient64.dll.
        HANDLE thread =
            CreateThread(
                nullptr,
                0,
                ScannerThread,
                nullptr,
                0,
                nullptr
            );

        if (thread != nullptr)
        {
            SetThreadPriority(
                thread,
                THREAD_PRIORITY_HIGHEST
            );

            CloseHandle(thread);
        }

        // Proxy обычно уже загрузил overlay.
        // Повторный вызов здесь безопасно ничего не делает,
        // если модуль уже присутствует.
        EnsureOverlayLoaded();

        ResolveSteamClientOriginals();
        PatchSteamClientVersionsEarly();

        ResolveSteamApiOriginals();
        ProactivelyPatchSteamClientInterfaces();

        AcquireSRWLockExclusive(
            &g_patchLock
        );

        PatchGameModules();

        ReleaseSRWLockExclusive(
            &g_patchLock
        );

        return TRUE;
    }

    DWORD WINAPI EarlyBootstrapThread(
        LPVOID)
    {
        SetThreadPriority(
            GetCurrentThread(),
            THREAD_PRIORITY_HIGHEST
        );

        InitOnceExecuteOnce(
            &g_initOnce,
            InitializeOnce,
            nullptr,
            nullptr
        );

        return 0;
    }
}

// ============================================================
// Export, который уже умеет вызывать твой Proxy/WINMM.
// ============================================================

extern "C"
__declspec(dllexport)
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
// DllMain: тяжёлой работы здесь нет.
// ============================================================

BOOL APIENTRY DllMain(
    HMODULE module,
    DWORD reason,
    LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;

        DisableThreadLibraryCalls(
            module
        );

        // ВАЖНО v4:
        // больше не ждём, пока Proxy когда-нибудь вызовет
        // InitializeProxyModule() через timeBeginPeriod.
        //
        // Как только loader lock отпустится, этот поток сам
        // запускает SteamCompat и начинает ждать steamclient64.dll.
        HANDLE thread =
            CreateThread(
                nullptr,
                0,
                EarlyBootstrapThread,
                nullptr,
                0,
                nullptr
            );

        if (thread != nullptr)
        {
            SetThreadPriority(
                thread,
                THREAD_PRIORITY_HIGHEST
            );

            CloseHandle(thread);
        }
    }
    else if (
        reason ==
        DLL_PROCESS_DETACH
        ) {
        InterlockedExchange(
            &g_stop,
            TRUE
        );
    }

    return TRUE;
}
