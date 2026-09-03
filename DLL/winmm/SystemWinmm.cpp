#include "pch.h"

#include <windows.h>
#include <array>
#include <string>

void EnsureProxyModulesInitialized();

extern "C"
{
    extern FARPROC g_WinmmTargets[181];
    FARPROC WINAPI ResolveWinmmExport(DWORD index);
}

namespace
{
    constexpr DWORD kExportCount = 181;

    INIT_ONCE g_realWinmmInitOnce = INIT_ONCE_STATIC_INIT;
    HMODULE g_realWinmm = nullptr;

    constexpr std::array<const char*, kExportCount> kExportNames = {
    "CloseDriver",
    "DefDriverProc",
    "DriverCallback",
    "DrvGetModuleHandle",
    "GetDriverModuleHandle",
    "OpenDriver",
    "PlaySound",
    "PlaySoundA",
    "PlaySoundW",
    "SendDriverMessage",
    "WOWAppExit",
    "auxGetDevCapsA",
    "auxGetDevCapsW",
    "auxGetNumDevs",
    "auxGetVolume",
    "auxOutMessage",
    "auxSetVolume",
    "joyConfigChanged",
    "joyGetDevCapsA",
    "joyGetDevCapsW",
    "joyGetNumDevs",
    "joyGetPos",
    "joyGetPosEx",
    "joyGetThreshold",
    "joyReleaseCapture",
    "joySetCapture",
    "joySetThreshold",
    "mciDriverNotify",
    "mciDriverYield",
    "mciExecute",
    "mciFreeCommandResource",
    "mciGetCreatorTask",
    "mciGetDeviceIDA",
    "mciGetDeviceIDFromElementIDA",
    "mciGetDeviceIDFromElementIDW",
    "mciGetDeviceIDW",
    "mciGetDriverData",
    "mciGetErrorStringA",
    "mciGetErrorStringW",
    "mciGetYieldProc",
    "mciLoadCommandResource",
    "mciSendCommandA",
    "mciSendCommandW",
    "mciSendStringA",
    "mciSendStringW",
    "mciSetDriverData",
    "mciSetYieldProc",
    "midiConnect",
    "midiDisconnect",
    "midiInAddBuffer",
    "midiInClose",
    "midiInGetDevCapsA",
    "midiInGetDevCapsW",
    "midiInGetErrorTextA",
    "midiInGetErrorTextW",
    "midiInGetID",
    "midiInGetNumDevs",
    "midiInMessage",
    "midiInOpen",
    "midiInPrepareHeader",
    "midiInReset",
    "midiInStart",
    "midiInStop",
    "midiInUnprepareHeader",
    "midiOutCacheDrumPatches",
    "midiOutCachePatches",
    "midiOutClose",
    "midiOutGetDevCapsA",
    "midiOutGetDevCapsW",
    "midiOutGetErrorTextA",
    "midiOutGetErrorTextW",
    "midiOutGetID",
    "midiOutGetNumDevs",
    "midiOutGetVolume",
    "midiOutLongMsg",
    "midiOutMessage",
    "midiOutOpen",
    "midiOutPrepareHeader",
    "midiOutReset",
    "midiOutSetVolume",
    "midiOutShortMsg",
    "midiOutUnprepareHeader",
    "midiStreamClose",
    "midiStreamOpen",
    "midiStreamOut",
    "midiStreamPause",
    "midiStreamPosition",
    "midiStreamProperty",
    "midiStreamRestart",
    "midiStreamStop",
    "mixerClose",
    "mixerGetControlDetailsA",
    "mixerGetControlDetailsW",
    "mixerGetDevCapsA",
    "mixerGetDevCapsW",
    "mixerGetID",
    "mixerGetLineControlsA",
    "mixerGetLineControlsW",
    "mixerGetLineInfoA",
    "mixerGetLineInfoW",
    "mixerGetNumDevs",
    "mixerMessage",
    "mixerOpen",
    "mixerSetControlDetails",
    "mmDrvInstall",
    "mmGetCurrentTask",
    "mmTaskBlock",
    "mmTaskCreate",
    "mmTaskSignal",
    "mmTaskYield",
    "mmioAdvance",
    "mmioAscend",
    "mmioClose",
    "mmioCreateChunk",
    "mmioDescend",
    "mmioFlush",
    "mmioGetInfo",
    "mmioInstallIOProcA",
    "mmioInstallIOProcW",
    "mmioOpenA",
    "mmioOpenW",
    "mmioRead",
    "mmioRenameA",
    "mmioRenameW",
    "mmioSeek",
    "mmioSendMessage",
    "mmioSetBuffer",
    "mmioSetInfo",
    "mmioStringToFOURCCA",
    "mmioStringToFOURCCW",
    "mmioWrite",
    "mmsystemGetVersion",
    "sndPlaySoundA",
    "sndPlaySoundW",
    "timeBeginPeriod",
    "timeEndPeriod",
    "timeGetDevCaps",
    "timeGetSystemTime",
    "timeGetTime",
    "timeKillEvent",
    "timeSetEvent",
    "waveInAddBuffer",
    "waveInClose",
    "waveInGetDevCapsA",
    "waveInGetDevCapsW",
    "waveInGetErrorTextA",
    "waveInGetErrorTextW",
    "waveInGetID",
    "waveInGetNumDevs",
    "waveInGetPosition",
    "waveInMessage",
    "waveInOpen",
    "waveInPrepareHeader",
    "waveInReset",
    "waveInStart",
    "waveInStop",
    "waveInUnprepareHeader",
    "waveOutBreakLoop",
    "waveOutClose",
    "waveOutGetDevCapsA",
    "waveOutGetDevCapsW",
    "waveOutGetErrorTextA",
    "waveOutGetErrorTextW",
    "waveOutGetID",
    "waveOutGetNumDevs",
    "waveOutGetPitch",
    "waveOutGetPlaybackRate",
    "waveOutGetPosition",
    "waveOutGetVolume",
    "waveOutMessage",
    "waveOutOpen",
    "waveOutPause",
    "waveOutPrepareHeader",
    "waveOutReset",
    "waveOutRestart",
    "waveOutSetPitch",
    "waveOutSetPlaybackRate",
    "waveOutSetVolume",
    "waveOutUnprepareHeader",
    "waveOutWrite",
    nullptr
    };

    constexpr std::array<WORD, kExportCount> kExportOrdinals = {
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    2
    };

    FARPROC WINAPI MissingWinmmExport()
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return nullptr;
    }

    BOOL CALLBACK InitializeRealWinmm(
        PINIT_ONCE,
        PVOID,
        PVOID*)
    {
        wchar_t systemDirectory[MAX_PATH]{};

        const UINT length = GetSystemDirectoryW(
            systemDirectory,
            MAX_PATH
        );

        if (length == 0 || length >= MAX_PATH)
            return TRUE;

        std::wstring path(systemDirectory, length);

        if (!path.empty() && path.back() != L'\\')
            path.push_back(L'\\');

        path += L"winmm.dll";

        // ВАЖНО:
        // загружаем по ПОЛНОМУ пути к System32.
        // Поэтому это не наш локальный WINMM.dll рядом с игрой.
        g_realWinmm = LoadLibraryExW(
            path.c_str(),
            nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
            LOAD_LIBRARY_SEARCH_SYSTEM32
        );

        if (g_realWinmm == nullptr)
        {
            // Fallback для систем, где SEARCH-флаги недоступны/ограничены.
            g_realWinmm = LoadLibraryW(path.c_str());
        }

        if (g_realWinmm == nullptr)
            return TRUE;

        for (DWORD i = 0; i < kExportCount; ++i)
        {
            FARPROC proc = nullptr;

            if (kExportOrdinals[i] != 0)
            {
                proc = GetProcAddress(
                    g_realWinmm,
                    MAKEINTRESOURCEA(kExportOrdinals[i])
                );
            }
            else if (kExportNames[i] != nullptr)
            {
                proc = GetProcAddress(
                    g_realWinmm,
                    kExportNames[i]
                );
            }

            if (proc != nullptr)
                g_WinmmTargets[i] = proc;
        }

        return TRUE;
    }
}

extern "C"
{
    // Эту таблицу читает ProxyForwarders.asm.
    FARPROC g_WinmmTargets[181]{};

    FARPROC WINAPI ResolveWinmmExport(DWORD index)
    {
        // Сначала запускаем твой modules.txt loader.
        // Значит SteamCompat.dll всё равно успеет инициализироваться
        // перед первым настоящим вызовом winmm.
        EnsureProxyModulesInitialized();

        InitOnceExecuteOnce(
            &g_realWinmmInitOnce,
            InitializeRealWinmm,
            nullptr,
            nullptr
        );

        if (index >= kExportCount)
            return reinterpret_cast<FARPROC>(&MissingWinmmExport);

        FARPROC proc = g_WinmmTargets[index];

        if (proc == nullptr)
            proc = reinterpret_cast<FARPROC>(&MissingWinmmExport);

        return proc;
    }
}
