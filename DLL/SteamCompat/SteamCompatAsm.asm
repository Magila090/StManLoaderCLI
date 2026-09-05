; SteamCompatAsm.asm
; x64 / MASM
;
; v5:
; - SteamUtils007 object is patched synchronously BEFORE GetISteamUtils
;   returns it to the caller.
; - slots 0..9 are all guarded, so we do not depend on slot 2 existing
;   in the observed call path.
; - every slot first checks [R15+38h]:
;       REAL_APP_ID -> FAKE_APP_ID
; - generic slots then tail-jump to the exact original method, preserving
;   all original stack arguments.
; - observed slot 2 and slot 9 repeat the same check AFTER the original
;   method too.
;
; This is intentionally a guarded state patch: if [R15+38h] is not REAL,
; nothing is changed.

option casemap:none

EXTERN g_OriginalSteamUtils007Slots:QWORD
EXTERN g_OriginalSteamAppTicketSlot0:QWORD

EXTERN g_RealAppId:DWORD
EXTERN g_FakeAppId:DWORD

EXTERN PatchSteamUtilsR15Field:PROC

PUBLIC HookSteamUtils007Slot0
PUBLIC HookSteamUtils007Slot1
PUBLIC HookSteamUtils007Slot2
PUBLIC HookSteamUtils007Slot3
PUBLIC HookSteamUtils007Slot4
PUBLIC HookSteamUtils007Slot5
PUBLIC HookSteamUtils007Slot6
PUBLIC HookSteamUtils007Slot7
PUBLIC HookSteamUtils007Slot8
PUBLIC HookSteamUtils007Slot9
PUBLIC HookSteamAppTicketSlot0

.code


; ============================================================
; Generic PRE hook.
;
; Save all Windows x64 register arguments, call the guarded R15 patch,
; restore everything, restore RSP exactly, then JMP to original.
;
; Because this is a tail jump, any fifth+ arguments already on the
; caller's stack remain at exactly their original offsets.
; ============================================================

STEAMUTILS_PRE_TAIL MACRO FuncName:req, SlotIndex:req
FuncName PROC
    ; Entry RSP = 8 mod 16.
    ; 88h is 8 mod 16 -> aligned before CALL.
    sub     rsp, 88h

    mov     QWORD PTR [rsp + 20h], rcx
    mov     QWORD PTR [rsp + 28h], rdx
    mov     QWORD PTR [rsp + 30h], r8
    mov     QWORD PTR [rsp + 38h], r9

    movdqu  XMMWORD PTR [rsp + 40h], xmm0
    movdqu  XMMWORD PTR [rsp + 50h], xmm1
    movdqu  XMMWORD PTR [rsp + 60h], xmm2
    movdqu  XMMWORD PTR [rsp + 70h], xmm3

    mov     rcx, r15
    call    PatchSteamUtilsR15Field

    movdqu  xmm0, XMMWORD PTR [rsp + 40h]
    movdqu  xmm1, XMMWORD PTR [rsp + 50h]
    movdqu  xmm2, XMMWORD PTR [rsp + 60h]
    movdqu  xmm3, XMMWORD PTR [rsp + 70h]

    mov     rcx, QWORD PTR [rsp + 20h]
    mov     rdx, QWORD PTR [rsp + 28h]
    mov     r8,  QWORD PTR [rsp + 30h]
    mov     r9,  QWORD PTR [rsp + 38h]

    add     rsp, 88h

    jmp     QWORD PTR [g_OriginalSteamUtils007Slots + (SlotIndex * 8)]
FuncName ENDP
ENDM


; Slots whose signatures may include stack arguments:
; use the pure pre-hook + tail jump.
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot0, 0
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot1, 1
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot3, 3
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot4, 4
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot5, 5
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot6, 6
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot7, 7
STEAMUTILS_PRE_TAIL HookSteamUtils007Slot8, 8


; ============================================================
; slot 2
;
; SteamUtils007 slot 2 is an observed no-explicit-argument method.
; We do guarded patch both BEFORE and AFTER original.
; ============================================================

HookSteamUtils007Slot2 PROC
    sub     rsp, 0A8h

    mov     QWORD PTR [rsp + 20h], rcx
    mov     QWORD PTR [rsp + 28h], rdx
    mov     QWORD PTR [rsp + 30h], r8
    mov     QWORD PTR [rsp + 38h], r9

    movdqu  XMMWORD PTR [rsp + 40h], xmm0
    movdqu  XMMWORD PTR [rsp + 50h], xmm1
    movdqu  XMMWORD PTR [rsp + 60h], xmm2
    movdqu  XMMWORD PTR [rsp + 70h], xmm3

    ; PRE
    mov     rcx, r15
    call    PatchSteamUtilsR15Field

    ; Restore original arguments.
    movdqu  xmm0, XMMWORD PTR [rsp + 40h]
    movdqu  xmm1, XMMWORD PTR [rsp + 50h]
    movdqu  xmm2, XMMWORD PTR [rsp + 60h]
    movdqu  xmm3, XMMWORD PTR [rsp + 70h]

    mov     rcx, QWORD PTR [rsp + 20h]
    mov     rdx, QWORD PTR [rsp + 28h]
    mov     r8,  QWORD PTR [rsp + 30h]
    mov     r9,  QWORD PTR [rsp + 38h]

    call    QWORD PTR [g_OriginalSteamUtils007Slots + (2 * 8)]

    ; Preserve original return.
    mov     QWORD PTR [rsp + 80h], rax
    movdqu  XMMWORD PTR [rsp + 88h], xmm0

    ; POST
    mov     rcx, r15
    call    PatchSteamUtilsR15Field

    mov     rax, QWORD PTR [rsp + 80h]
    movdqu  xmm0, XMMWORD PTR [rsp + 88h]

    add     rsp, 0A8h
    ret
HookSteamUtils007Slot2 ENDP


; ============================================================
; slot 9 = the proven Frida onLeave location.
;
; It is also a no-explicit-argument SteamUtils007 method in this interface.
; Guard both BEFORE and AFTER.
; ============================================================

HookSteamUtils007Slot9 PROC
    sub     rsp, 0A8h

    mov     QWORD PTR [rsp + 20h], rcx
    mov     QWORD PTR [rsp + 28h], rdx
    mov     QWORD PTR [rsp + 30h], r8
    mov     QWORD PTR [rsp + 38h], r9

    movdqu  XMMWORD PTR [rsp + 40h], xmm0
    movdqu  XMMWORD PTR [rsp + 50h], xmm1
    movdqu  XMMWORD PTR [rsp + 60h], xmm2
    movdqu  XMMWORD PTR [rsp + 70h], xmm3

    ; PRE
    mov     rcx, r15
    call    PatchSteamUtilsR15Field

    movdqu  xmm0, XMMWORD PTR [rsp + 40h]
    movdqu  xmm1, XMMWORD PTR [rsp + 50h]
    movdqu  xmm2, XMMWORD PTR [rsp + 60h]
    movdqu  xmm3, XMMWORD PTR [rsp + 70h]

    mov     rcx, QWORD PTR [rsp + 20h]
    mov     rdx, QWORD PTR [rsp + 28h]
    mov     r8,  QWORD PTR [rsp + 30h]
    mov     r9,  QWORD PTR [rsp + 38h]

    call    QWORD PTR [g_OriginalSteamUtils007Slots + (9 * 8)]

    ; Preserve exact original return values.
    mov     QWORD PTR [rsp + 80h], rax
    movdqu  XMMWORD PTR [rsp + 88h], xmm0

    ; POST = exact equivalent of the useful Frida onLeave check.
    mov     rcx, r15
    call    PatchSteamUtilsR15Field

    mov     rax, QWORD PTR [rsp + 80h]
    movdqu  xmm0, XMMWORD PTR [rsp + 88h]

    add     rsp, 0A8h
    ret
HookSteamUtils007Slot9 ENDP


; ============================================================
; Steam App Ticket
; ============================================================

HookSteamAppTicketSlot0 PROC
    ; RCX = this
    ; EDX = first explicit AppID_t argument

    mov     eax, DWORD PTR [g_RealAppId]
    cmp     edx, eax
    jne     AppTicketJump

    mov     edx, DWORD PTR [g_FakeAppId]

AppTicketJump:
    jmp     QWORD PTR [g_OriginalSteamAppTicketSlot0]
HookSteamAppTicketSlot0 ENDP


END
