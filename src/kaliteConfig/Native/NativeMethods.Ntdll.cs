using System;
using System.Runtime.InteropServices;

namespace kaliteConfig.Native;

internal static partial class NativeMethods
{
    internal static partial class Ntdll
    {
        internal const int SystemProcessInformation = 5;
        internal const int ThreadQuerySetWin32StartAddress = 9;
        internal const int ThreadNameInformation = 38;
        internal const int StatusBufferOverflow = unchecked((int)0x80000005);
        internal const int StatusBufferTooSmall = unchecked((int)0xC0000023);
        internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        internal const int ProcessIoPriority = 33;
        // ThreadIoPriority is information class 22; 33 on a thread handle is
        // ThreadIdealProcessorEx and returns a processor number, not an I/O hint.
        internal const int ThreadIoPriority = 22;

        // IO_PRIORITY_HINT values. VeryLow/Low/Normal are always settable; High
        // usually needs elevation and Critical is never offered by the app.
        internal const uint IoPriorityVeryLow = 0;
        internal const uint IoPriorityLow = 1;
        internal const uint IoPriorityNormal = 2;
        internal const uint IoPriorityHigh = 3;

        [LibraryImport("ntdll.dll")]
        internal static partial int NtSetInformationProcess(
            SafeProcessHandle processHandle,
            int processInformationClass,
            ref uint processInformation,
            uint processInformationLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtSetInformationThread(
            SafeThreadHandle threadHandle,
            int threadInformationClass,
            ref uint threadInformation,
            uint threadInformationLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationThread(
            SafeThreadHandle threadHandle,
            int threadInformationClass,
            ref uint threadInformation,
            uint threadInformationLength,
            out uint returnLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationProcess(
            SafeProcessHandle processHandle,
            int processInformationClass,
            ref uint processInformation,
            uint processInformationLength,
            out uint returnLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQuerySystemInformation(
            int systemInformationClass,
            [Out] byte[]? systemInformation,
            uint systemInformationLength,
            out uint returnLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationThread(
            SafeThreadHandle thread,
            int threadInformationClass,
            out IntPtr threadInformation,
            int threadInformationLength,
            out uint returnLength);

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationThreadName(
            SafeThreadHandle thread,
            int threadInformationClass,
            IntPtr threadInformation,
            int threadInformationLength,
            out uint returnLength);
    }
}
