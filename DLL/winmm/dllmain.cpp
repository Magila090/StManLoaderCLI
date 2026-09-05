#include "pch.h"

#include <windows.h>
#include <cstring>

extern "C" IMAGE_DOS_HEADER __ImageBase;

using ProxyModuleInitializeFunc = void(*)();

namespace
{
    INIT_ONCE g_modulesInitOnce = INIT_ONCE_STATIC_INIT;

    HMODULE g_earlySteamOverlay = nullptr;
    HMODULE g_earlySteamCompat = nullptr;

    constexpr DWORD kMaxModulesFileBytes = 64 * 1024;
    constexpr size_t kMaxInitializedModules = 64;

    // ------------------------------------------------------------
    // Basic path helpers. No STL / iostream / heap allocations here.
    // ------------------------------------------------------------

    bool GetProxyDirectoryW(
        wchar_t* output,
        size_t capacity)
    {
        if (
            output == nullptr ||
            capacity < 2
            ) {
            return false;
        }

        const DWORD length =
            GetModuleFileNameW(
                reinterpret_cast<HMODULE>(
                    &__ImageBase
                    ),
                output,
                static_cast<DWORD>(capacity)
            );

        if (
            length == 0 ||
            length >= capacity
            ) {
            output[0] = L'\0';
            return false;
        }

        for (DWORD i = length; i > 0; --i)
        {
            const DWORD pos = i - 1;

            if (
                output[pos] == L'\\' ||
                output[pos] == L'/'
                ) {
                output[pos + 1] = L'\0';
                return true;
            }
        }

        output[0] = L'\0';
        return false;
    }

    bool GetProxyDirectoryA(
        char* output,
        size_t capacity)
    {
        if (
            output == nullptr ||
            capacity < 2
            ) {
            return false;
        }

        const DWORD length =
            GetModuleFileNameA(
                reinterpret_cast<HMODULE>(
                    &__ImageBase
                    ),
                output,
                static_cast<DWORD>(capacity)
            );

        if (
            length == 0 ||
            length >= capacity
            ) {
            output[0] = '\0';
            return false;
        }

        for (DWORD i = length; i > 0; --i)
        {
            const DWORD pos = i - 1;

            if (
                output[pos] == '\\' ||
                output[pos] == '/'
                ) {
                output[pos + 1] = '\0';
                return true;
            }
        }

        output[0] = '\0';
        return false;
    }

    bool AppendW(
        wchar_t* buffer,
        size_t capacity,
        const wchar_t* suffix)
    {
        if (
            buffer == nullptr ||
            suffix == nullptr
            ) {
            return false;
        }

        const size_t current =
            static_cast<size_t>(
                lstrlenW(buffer)
                );

        const size_t extra =
            static_cast<size_t>(
                lstrlenW(suffix)
                );

        if (
            current + extra + 1 >
            capacity
            ) {
            return false;
        }

        memcpy(
            buffer + current,
            suffix,
            (extra + 1) *
            sizeof(wchar_t)
        );

        return true;
    }

    bool AppendA(
        char* buffer,
        size_t capacity,
        const char* suffix)
    {
        if (
            buffer == nullptr ||
            suffix == nullptr
            ) {
            return false;
        }

        const size_t current =
            std::strlen(buffer);

        const size_t extra =
            std::strlen(suffix);

        if (
            current + extra + 1 >
            capacity
            ) {
            return false;
        }

        memcpy(
            buffer + current,
            suffix,
            extra + 1
        );

        return true;
    }

    // ------------------------------------------------------------
    // Very early Steam context / overlay.
    // ------------------------------------------------------------

    void PrepareSteamVeryEarly()
    {
        SetEnvironmentVariableW(
            L"SteamClientLaunch",
            L"1"
        );

        SetEnvironmentVariableW(
            L"SteamGameId",
            L"480"
        );

        SetEnvironmentVariableW(
            L"SteamAppId",
            L"480"
        );

        SetEnvironmentVariableW(
            L"SteamOverlayGameId",
            L"480"
        );

        SetEnvironmentVariableW(
            L"SteamEnv",
            L"1"
        );

        g_earlySteamOverlay =
            GetModuleHandleW(
                L"gameoverlayrenderer64.dll"
            );

        if (g_earlySteamOverlay == nullptr)
        {
            g_earlySteamOverlay =
                LoadLibraryW(
                    L"C:\\Program Files (x86)\\Steam\\"
                    L"gameoverlayrenderer64.dll"
                );
        }

        if (g_earlySteamOverlay != nullptr)
        {
            HMODULE pinned = nullptr;

            GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_PIN,
                L"gameoverlayrenderer64.dll",
                &pinned
            );
        }
    }

    // ------------------------------------------------------------
    // SteamCompat must be resident before steamclient64.dll appears.
    // ------------------------------------------------------------

    void LoadSteamCompatVeryEarly()
    {
        if (
            GetModuleHandleW(
                L"SteamCompat.dll"
            ) != nullptr
            ) {
            return;
        }

        wchar_t path[MAX_PATH]{};

        if (
            !GetProxyDirectoryW(
                path,
                MAX_PATH
            )
            ) {
            return;
        }

        if (
            !AppendW(
                path,
                MAX_PATH,
                L"SteamCompat.dll"
            )
            ) {
            return;
        }

        g_earlySteamCompat =
            LoadLibraryW(path);
    }

    // ------------------------------------------------------------
    // modules.txt parser.
    // LoadModules is called only once through InitOnce, so an STL set
    // is unnecessary. A small fixed HMODULE array avoids duplicate init.
    // ------------------------------------------------------------

    char* TrimLine(char* line)
    {
        if (line == nullptr)
            return nullptr;

        while (
            *line == ' ' ||
            *line == '\t' ||
            *line == '\r' ||
            *line == '\n'
            ) {
            ++line;
        }

        char* end =
            line + std::strlen(line);

        while (end > line)
        {
            const char ch =
                *(end - 1);

            if (
                ch != ' ' &&
                ch != '\t' &&
                ch != '\r' &&
                ch != '\n'
                ) {
                break;
            }

            --end;
        }

        *end = '\0';
        return line;
    }

    bool IsValidModuleName(
        const char* name)
    {
        if (
            name == nullptr ||
            *name == '\0'
            ) {
            return false;
        }

        for (
            const char* p = name;
            *p != '\0';
            ++p
            ) {
            if (
                *p == '\\' ||
                *p == '/' ||
                *p == ':'
                ) {
                return false;
            }
        }

        return true;
    }

    bool WasInitialized(
        HMODULE module,
        HMODULE* initialized,
        size_t count)
    {
        for (size_t i = 0; i < count; ++i)
        {
            if (initialized[i] == module)
                return true;
        }

        return false;
    }

    void InitializeModule(
        HMODULE module,
        HMODULE* initialized,
        size_t& initializedCount)
    {
        if (module == nullptr)
            return;

        if (
            WasInitialized(
                module,
                initialized,
                initializedCount
            )
            ) {
            return;
        }

        if (
            initializedCount <
            kMaxInitializedModules
            ) {
            initialized[
                initializedCount++
            ] = module;
        }

        const auto initialize =
            reinterpret_cast<
            ProxyModuleInitializeFunc
            >(
                GetProcAddress(
                    module,
                    "InitializeProxyModule"
                )
                );

        if (initialize != nullptr)
            initialize();
    }

    void LoadModules()
    {
        char directory[MAX_PATH]{};

        if (
            !GetProxyDirectoryA(
                directory,
                MAX_PATH
            )
            ) {
            return;
        }

        char modulesPath[MAX_PATH]{};

        lstrcpynA(
            modulesPath,
            directory,
            MAX_PATH
        );

        if (
            !AppendA(
                modulesPath,
                MAX_PATH,
                "modules.txt"
            )
            ) {
            return;
        }

        HANDLE file =
            CreateFileA(
                modulesPath,
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
            return;

        LARGE_INTEGER fileSize{};

        if (
            !GetFileSizeEx(
                file,
                &fileSize
            ) ||
            fileSize.QuadPart <= 0 ||
            fileSize.QuadPart >
            kMaxModulesFileBytes
            ) {
            CloseHandle(file);
            return;
        }

        const SIZE_T allocationSize =
            static_cast<SIZE_T>(
                fileSize.QuadPart
                ) + 1;

        char* data =
            static_cast<char*>(
                HeapAlloc(
                    GetProcessHeap(),
                    0,
                    allocationSize
                )
                );

        if (data == nullptr)
        {
            CloseHandle(file);
            return;
        }

        DWORD bytesRead = 0;

        const BOOL readOk =
            ReadFile(
                file,
                data,
                static_cast<DWORD>(
                    fileSize.QuadPart
                    ),
                &bytesRead,
                nullptr
            );

        CloseHandle(file);

        if (!readOk)
        {
            HeapFree(
                GetProcessHeap(),
                0,
                data
            );

            return;
        }

        data[bytesRead] = '\0';

        // Strip UTF-8 BOM if present.
        char* cursor = data;

        if (
            bytesRead >= 3 &&
            static_cast<unsigned char>(
                data[0]
                ) == 0xEF &&
            static_cast<unsigned char>(
                data[1]
                ) == 0xBB &&
            static_cast<unsigned char>(
                data[2]
                ) == 0xBF
            ) {
            cursor += 3;
        }

        HMODULE initialized[
            kMaxInitializedModules
        ]{};

            size_t initializedCount = 0;

            while (*cursor != '\0')
            {
                char* line = cursor;

                while (
                    *cursor != '\0' &&
                    *cursor != '\r' &&
                    *cursor != '\n'
                    ) {
                    ++cursor;
                }

                if (*cursor != '\0')
                {
                    *cursor++ = '\0';

                    while (
                        *cursor == '\r' ||
                        *cursor == '\n'
                        ) {
                        ++cursor;
                    }
                }

                line = TrimLine(line);

                if (
                    line == nullptr ||
                    *line == '\0' ||
                    *line == '#' ||
                    *line == ';' ||
                    !IsValidModuleName(line)
                    ) {
                    continue;
                }

                char fullPath[MAX_PATH]{};

                lstrcpynA(
                    fullPath,
                    directory,
                    MAX_PATH
                );

                if (
                    !AppendA(
                        fullPath,
                        MAX_PATH,
                        line
                    )
                    ) {
                    continue;
                }

                HMODULE module =
                    GetModuleHandleA(line);

                if (module == nullptr)
                    module = LoadLibraryA(fullPath);

                InitializeModule(
                    module,
                    initialized,
                    initializedCount
                );
            }

            HeapFree(
                GetProcessHeap(),
                0,
                data
            );
    }

    BOOL CALLBACK InitializeModulesOnce(
        PINIT_ONCE,
        PVOID,
        PVOID*)
    {
        LoadModules();
        return TRUE;
    }
}

// Called by the local WINMM wrapper before forwarding.
void EnsureProxyModulesInitialized()
{
    InitOnceExecuteOnce(
        &g_modulesInitOnce,
        InitializeModulesOnce,
        nullptr,
        nullptr
    );
}

BOOL APIENTRY DllMain(
    HMODULE hModule,
    DWORD reason,
    LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        PrepareSteamVeryEarly();

        // Keep the working v5 timing: SteamCompat is resident before
        // steamclient64.dll / steam_api64.dll appear.
        LoadSteamCompatVeryEarly();

        DisableThreadLibraryCalls(
            hModule
        );
    }

    return TRUE;
}
