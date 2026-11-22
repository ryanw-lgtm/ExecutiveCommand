/* Definitions missing from older MinGW-w64 headers (e.g. Ubuntu 24.04's), matching the Windows SDK.
   Used only when cross-compiling llama.cpp for Windows; see tools/build_windows_llama.sh. */
#pragma once
#if defined(_WIN32)
#include <windows.h>
#ifndef THREAD_POWER_THROTTLING_CURRENT_VERSION
typedef struct _THREAD_POWER_THROTTLING_STATE { ULONG Version; ULONG ControlMask; ULONG StateMask; } THREAD_POWER_THROTTLING_STATE;
#define THREAD_POWER_THROTTLING_CURRENT_VERSION 1
#define THREAD_POWER_THROTTLING_EXECUTION_SPEED 0x1
#endif
#endif
