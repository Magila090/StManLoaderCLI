; ============================================================
; ProxyForwarders.asm
; x64 signature-independent trampolines for WINMM proxy.
;
; Fast path:
;   jump directly to cached System32 winmm.dll function.
;
; First call:
;   save RCX/RDX/R8/R9 + XMM0..XMM3,
;   call ResolveWinmmExport(index),
;   restore original arguments and jump to the real function.
; ============================================================

option casemap:none

EXTERN g_WinmmTargets:QWORD
EXTERN ResolveWinmmExport:PROC

.code

WINMM_THUNK MACRO FuncName:req, Index:req
LOCAL ready
FuncName PROC
    mov     rax, QWORD PTR [g_WinmmTargets + (Index * 8)]
    test    rax, rax
    jnz     ready

    ; Entry RSP is 8 mod 16.
    ; 88h keeps it 16-byte aligned before CALL and gives shadow space.
    sub     rsp, 88h

    mov     QWORD PTR [rsp + 20h], rcx
    mov     QWORD PTR [rsp + 28h], rdx
    mov     QWORD PTR [rsp + 30h], r8
    mov     QWORD PTR [rsp + 38h], r9

    movdqu  XMMWORD PTR [rsp + 40h], xmm0
    movdqu  XMMWORD PTR [rsp + 50h], xmm1
    movdqu  XMMWORD PTR [rsp + 60h], xmm2
    movdqu  XMMWORD PTR [rsp + 70h], xmm3

    mov     ecx, Index
    call    ResolveWinmmExport
    mov     r11, rax

    movdqu  xmm0, XMMWORD PTR [rsp + 40h]
    movdqu  xmm1, XMMWORD PTR [rsp + 50h]
    movdqu  xmm2, XMMWORD PTR [rsp + 60h]
    movdqu  xmm3, XMMWORD PTR [rsp + 70h]

    mov     rcx, QWORD PTR [rsp + 20h]
    mov     rdx, QWORD PTR [rsp + 28h]
    mov     r8,  QWORD PTR [rsp + 30h]
    mov     r9,  QWORD PTR [rsp + 38h]

    add     rsp, 88h
    jmp     r11

ready:
    jmp     rax
FuncName ENDP
ENDM

WINMM_THUNK CloseDriver, 0
WINMM_THUNK DefDriverProc, 1
WINMM_THUNK DriverCallback, 2
WINMM_THUNK DrvGetModuleHandle, 3
WINMM_THUNK GetDriverModuleHandle, 4
WINMM_THUNK OpenDriver, 5
WINMM_THUNK PlaySound, 6
WINMM_THUNK PlaySoundA, 7
WINMM_THUNK PlaySoundW, 8
WINMM_THUNK SendDriverMessage, 9
WINMM_THUNK WOWAppExit, 10
WINMM_THUNK auxGetDevCapsA, 11
WINMM_THUNK auxGetDevCapsW, 12
WINMM_THUNK auxGetNumDevs, 13
WINMM_THUNK auxGetVolume, 14
WINMM_THUNK auxOutMessage, 15
WINMM_THUNK auxSetVolume, 16
WINMM_THUNK joyConfigChanged, 17
WINMM_THUNK joyGetDevCapsA, 18
WINMM_THUNK joyGetDevCapsW, 19
WINMM_THUNK joyGetNumDevs, 20
WINMM_THUNK joyGetPos, 21
WINMM_THUNK joyGetPosEx, 22
WINMM_THUNK joyGetThreshold, 23
WINMM_THUNK joyReleaseCapture, 24
WINMM_THUNK joySetCapture, 25
WINMM_THUNK joySetThreshold, 26
WINMM_THUNK mciDriverNotify, 27
WINMM_THUNK mciDriverYield, 28
WINMM_THUNK mciExecute, 29
WINMM_THUNK mciFreeCommandResource, 30
WINMM_THUNK mciGetCreatorTask, 31
WINMM_THUNK mciGetDeviceIDA, 32
WINMM_THUNK mciGetDeviceIDFromElementIDA, 33
WINMM_THUNK mciGetDeviceIDFromElementIDW, 34
WINMM_THUNK mciGetDeviceIDW, 35
WINMM_THUNK mciGetDriverData, 36
WINMM_THUNK mciGetErrorStringA, 37
WINMM_THUNK mciGetErrorStringW, 38
WINMM_THUNK mciGetYieldProc, 39
WINMM_THUNK mciLoadCommandResource, 40
WINMM_THUNK mciSendCommandA, 41
WINMM_THUNK mciSendCommandW, 42
WINMM_THUNK mciSendStringA, 43
WINMM_THUNK mciSendStringW, 44
WINMM_THUNK mciSetDriverData, 45
WINMM_THUNK mciSetYieldProc, 46
WINMM_THUNK midiConnect, 47
WINMM_THUNK midiDisconnect, 48
WINMM_THUNK midiInAddBuffer, 49
WINMM_THUNK midiInClose, 50
WINMM_THUNK midiInGetDevCapsA, 51
WINMM_THUNK midiInGetDevCapsW, 52
WINMM_THUNK midiInGetErrorTextA, 53
WINMM_THUNK midiInGetErrorTextW, 54
WINMM_THUNK midiInGetID, 55
WINMM_THUNK midiInGetNumDevs, 56
WINMM_THUNK midiInMessage, 57
WINMM_THUNK midiInOpen, 58
WINMM_THUNK midiInPrepareHeader, 59
WINMM_THUNK midiInReset, 60
WINMM_THUNK midiInStart, 61
WINMM_THUNK midiInStop, 62
WINMM_THUNK midiInUnprepareHeader, 63
WINMM_THUNK midiOutCacheDrumPatches, 64
WINMM_THUNK midiOutCachePatches, 65
WINMM_THUNK midiOutClose, 66
WINMM_THUNK midiOutGetDevCapsA, 67
WINMM_THUNK midiOutGetDevCapsW, 68
WINMM_THUNK midiOutGetErrorTextA, 69
WINMM_THUNK midiOutGetErrorTextW, 70
WINMM_THUNK midiOutGetID, 71
WINMM_THUNK midiOutGetNumDevs, 72
WINMM_THUNK midiOutGetVolume, 73
WINMM_THUNK midiOutLongMsg, 74
WINMM_THUNK midiOutMessage, 75
WINMM_THUNK midiOutOpen, 76
WINMM_THUNK midiOutPrepareHeader, 77
WINMM_THUNK midiOutReset, 78
WINMM_THUNK midiOutSetVolume, 79
WINMM_THUNK midiOutShortMsg, 80
WINMM_THUNK midiOutUnprepareHeader, 81
WINMM_THUNK midiStreamClose, 82
WINMM_THUNK midiStreamOpen, 83
WINMM_THUNK midiStreamOut, 84
WINMM_THUNK midiStreamPause, 85
WINMM_THUNK midiStreamPosition, 86
WINMM_THUNK midiStreamProperty, 87
WINMM_THUNK midiStreamRestart, 88
WINMM_THUNK midiStreamStop, 89
WINMM_THUNK mixerClose, 90
WINMM_THUNK mixerGetControlDetailsA, 91
WINMM_THUNK mixerGetControlDetailsW, 92
WINMM_THUNK mixerGetDevCapsA, 93
WINMM_THUNK mixerGetDevCapsW, 94
WINMM_THUNK mixerGetID, 95
WINMM_THUNK mixerGetLineControlsA, 96
WINMM_THUNK mixerGetLineControlsW, 97
WINMM_THUNK mixerGetLineInfoA, 98
WINMM_THUNK mixerGetLineInfoW, 99
WINMM_THUNK mixerGetNumDevs, 100
WINMM_THUNK mixerMessage, 101
WINMM_THUNK mixerOpen, 102
WINMM_THUNK mixerSetControlDetails, 103
WINMM_THUNK mmDrvInstall, 104
WINMM_THUNK mmGetCurrentTask, 105
WINMM_THUNK mmTaskBlock, 106
WINMM_THUNK mmTaskCreate, 107
WINMM_THUNK mmTaskSignal, 108
WINMM_THUNK mmTaskYield, 109
WINMM_THUNK mmioAdvance, 110
WINMM_THUNK mmioAscend, 111
WINMM_THUNK mmioClose, 112
WINMM_THUNK mmioCreateChunk, 113
WINMM_THUNK mmioDescend, 114
WINMM_THUNK mmioFlush, 115
WINMM_THUNK mmioGetInfo, 116
WINMM_THUNK mmioInstallIOProcA, 117
WINMM_THUNK mmioInstallIOProcW, 118
WINMM_THUNK mmioOpenA, 119
WINMM_THUNK mmioOpenW, 120
WINMM_THUNK mmioRead, 121
WINMM_THUNK mmioRenameA, 122
WINMM_THUNK mmioRenameW, 123
WINMM_THUNK mmioSeek, 124
WINMM_THUNK mmioSendMessage, 125
WINMM_THUNK mmioSetBuffer, 126
WINMM_THUNK mmioSetInfo, 127
WINMM_THUNK mmioStringToFOURCCA, 128
WINMM_THUNK mmioStringToFOURCCW, 129
WINMM_THUNK mmioWrite, 130
WINMM_THUNK mmsystemGetVersion, 131
WINMM_THUNK sndPlaySoundA, 132
WINMM_THUNK sndPlaySoundW, 133
WINMM_THUNK timeBeginPeriod, 134
WINMM_THUNK timeEndPeriod, 135
WINMM_THUNK timeGetDevCaps, 136
WINMM_THUNK timeGetSystemTime, 137
WINMM_THUNK timeGetTime, 138
WINMM_THUNK timeKillEvent, 139
WINMM_THUNK timeSetEvent, 140
WINMM_THUNK waveInAddBuffer, 141
WINMM_THUNK waveInClose, 142
WINMM_THUNK waveInGetDevCapsA, 143
WINMM_THUNK waveInGetDevCapsW, 144
WINMM_THUNK waveInGetErrorTextA, 145
WINMM_THUNK waveInGetErrorTextW, 146
WINMM_THUNK waveInGetID, 147
WINMM_THUNK waveInGetNumDevs, 148
WINMM_THUNK waveInGetPosition, 149
WINMM_THUNK waveInMessage, 150
WINMM_THUNK waveInOpen, 151
WINMM_THUNK waveInPrepareHeader, 152
WINMM_THUNK waveInReset, 153
WINMM_THUNK waveInStart, 154
WINMM_THUNK waveInStop, 155
WINMM_THUNK waveInUnprepareHeader, 156
WINMM_THUNK waveOutBreakLoop, 157
WINMM_THUNK waveOutClose, 158
WINMM_THUNK waveOutGetDevCapsA, 159
WINMM_THUNK waveOutGetDevCapsW, 160
WINMM_THUNK waveOutGetErrorTextA, 161
WINMM_THUNK waveOutGetErrorTextW, 162
WINMM_THUNK waveOutGetID, 163
WINMM_THUNK waveOutGetNumDevs, 164
WINMM_THUNK waveOutGetPitch, 165
WINMM_THUNK waveOutGetPlaybackRate, 166
WINMM_THUNK waveOutGetPosition, 167
WINMM_THUNK waveOutGetVolume, 168
WINMM_THUNK waveOutMessage, 169
WINMM_THUNK waveOutOpen, 170
WINMM_THUNK waveOutPause, 171
WINMM_THUNK waveOutPrepareHeader, 172
WINMM_THUNK waveOutReset, 173
WINMM_THUNK waveOutRestart, 174
WINMM_THUNK waveOutSetPitch, 175
WINMM_THUNK waveOutSetPlaybackRate, 176
WINMM_THUNK waveOutSetVolume, 177
WINMM_THUNK waveOutUnprepareHeader, 178
WINMM_THUNK waveOutWrite, 179
WINMM_THUNK Ordinal_2, 180

END
