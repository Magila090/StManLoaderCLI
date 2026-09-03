#include "pch.h"

#include <windows.h>
#include <fstream>
#include <string>
#include <set>
#include <mutex>

extern "C" IMAGE_DOS_HEADER __ImageBase;

static std::set<std::string> g_loadedModules;

using ProxyModuleInitializeFunc = void(*)();


// ============================================================
// Очень ранняя подготовка Steam Overlay
// ============================================================

static HMODULE g_earlySteamOverlay = nullptr;

static void PrepareSteamOverlayVeryEarly()
{
    // Steam-контекст, который раньше задавал Python до запуска игры.
    SetEnvironmentVariableW(L"SteamClientLaunch", L"1");
    SetEnvironmentVariableW(L"SteamGameId", L"480");
    SetEnvironmentVariableW(L"SteamAppId", L"480");
    SetEnvironmentVariableW(L"SteamOverlayGameId", L"480");
    SetEnvironmentVariableW(L"SteamEnv", L"1");

    g_earlySteamOverlay =
        GetModuleHandleW(L"gameoverlayrenderer64.dll");

    if (g_earlySteamOverlay == nullptr)
    {
        g_earlySteamOverlay = LoadLibraryW(
            L"C:\\Program Files (x86)\\Steam\\gameoverlayrenderer64.dll"
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


// ============================================================
// Инициализация модулей выполняется ровно один раз
// ============================================================

static INIT_ONCE g_modulesInitOnce = INIT_ONCE_STATIC_INIT;


// ============================================================
// Убираем пробелы
// ============================================================

std::string Trim(const std::string& str)
{
    size_t start = str.find_first_not_of(" \t\r\n");

    if (start == std::string::npos)
        return "";

    size_t end = str.find_last_not_of(" \t\r\n");

    return str.substr(start, end - start + 1);
}


// ============================================================
// Получаем папку Proxy.dll
// ============================================================

std::string GetProxyDirectory()
{
    char path[MAX_PATH]{};

    DWORD length = GetModuleFileNameA(
        reinterpret_cast<HMODULE>(&__ImageBase),
        path,
        MAX_PATH
    );

    if (length == 0 || length >= MAX_PATH)
        return "";

    std::string fullPath(path);

    size_t lastSlash = fullPath.find_last_of("\\/");

    if (lastSlash == std::string::npos)
        return "";

    return fullPath.substr(0, lastSlash + 1);
}


// ============================================================
// Проверяем имя DLL
// ============================================================

bool IsValidModuleName(const std::string& name)
{
    if (name.empty())
        return false;

    // modules.txt должен содержать только имя DLL,
    // а не произвольный путь.
    if (name.find('\\') != std::string::npos ||
        name.find('/') != std::string::npos ||
        name.find(':') != std::string::npos)
    {
        return false;
    }

    return true;
}


// ============================================================
// Загружаем DLL из modules.txt
// ============================================================

void LoadModules()
{
    const std::string directory = GetProxyDirectory();

    if (directory.empty())
        return;

    const std::string txtPath =
        directory + "modules.txt";

    std::ifstream file(txtPath);

    if (!file.is_open())
        return;

    std::string line;

    while (std::getline(file, line))
    {
        line = Trim(line);

        if (line.empty())
            continue;

        if (!IsValidModuleName(line))
            continue;

        if (g_loadedModules.find(line) !=
            g_loadedModules.end())
        {
            continue;
        }

        const std::string dllPath =
            directory + line;

        HMODULE module =
            LoadLibraryA(dllPath.c_str());

        if (!module)
            continue;

        g_loadedModules.insert(line);

        auto initialize =
            reinterpret_cast<ProxyModuleInitializeFunc>(
                GetProcAddress(
                    module,
                    "InitializeProxyModule"
                )
                );

        // ВАЖНО:
        // этот вызов происходит уже НЕ из DllMain.
        // Текущий поток игры ждёт возврата функции.
        if (initialize)
        {
            initialize();
        }
    }
}


// ============================================================
// InitOnce callback
// ============================================================

BOOL CALLBACK InitializeModulesOnce(
    PINIT_ONCE,
    PVOID,
    PVOID*)
{
    LoadModules();

    return TRUE;
}


// ============================================================
// Вызывать перед передачей управления настоящей winmm.dll
// ============================================================

void EnsureProxyModulesInitialized()
{
    InitOnceExecuteOnce(
        &g_modulesInitOnce,
        InitializeModulesOnce,
        nullptr,
        nullptr
    );
}


// ============================================================
// DllMain
// ============================================================

BOOL APIENTRY DllMain(
    HMODULE hModule,
    DWORD reason,
    LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        // Сначала максимально рано подготавливаем Steam-контекст и overlay.
        PrepareSteamOverlayVeryEarly();

        DisableThreadLibraryCalls(hModule);

        // Остальные модули из modules.txt по-прежнему загружаются
        // через EnsureProxyModulesInitialized().
    }

    return TRUE;
}