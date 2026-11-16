using System;
using System.Runtime.InteropServices;

namespace ImpossibleRobert.Common.Integration.FMOD
{
    internal enum FmodResult
    {
        OK = 0,
        ERR_FILE_BAD = 13,
        ERR_FILE_COULDNOTSEEK = 14,
        ERR_FILE_EOF = 16,
        ERR_FILE_ENDOFDATA = 17,
        ERR_FILE_NOTFOUND = 18,
        ERR_FORMAT = 19,
        ERR_HEADER_MISMATCH = 20,
        ERR_INITIALIZATION = 26,
        ERR_INTERNAL = 28,
        ERR_INVALID_HANDLE = 30,
        ERR_INVALID_PARAM = 31,
        ERR_INVALID_POSITION = 32,
        ERR_MEMORY = 38,
        ERR_NOTREADY = 46,
        ERR_OUTPUT_ALLOCATED = 47,
        ERR_OUTPUT_CREATEBUFFER = 48,
        ERR_OUTPUT_DRIVERCALL = 49,
        ERR_OUTPUT_FORMAT = 50,
        ERR_OUTPUT_INIT = 51,
        ERR_OUTPUT_NODRIVERS = 52,
        ERR_PLUGIN_MISSING = 54,
        ERR_TRUNCATED = 65,
        ERR_UNSUPPORTED = 68,
        ERR_VERSION = 69
    }

    internal enum FmodOutput
    {
        Autodetect = 0,
        Unknown = 1,
        NoSound = 2,
        WaveWriter = 3,
        NoSoundNonRealtime = 4,
        WaveWriterNonRealtime = 5
    }

    internal enum FmodSampleFormat
    {
        None = 0,
        Pcm8 = 1,
        Pcm16 = 2,
        Pcm24 = 3,
        Pcm32 = 4,
        PcmFloat = 5,
        Bitstream = 6
    }

    internal enum FmodOpenState
    {
        Ready = 0,
        Error = 2
    }

    [Flags]
    internal enum FmodMode : uint
    {
        LoopOff = 0x00000001,
        LoopNormal = 0x00000002,
        TwoDimensional = 0x00000008,
        CreateSample = 0x00000100,
        OpenMemory = 0x00000800,
        OpenRaw = 0x00001000,
        OpenOnly = 0x00002000,
        AccurateTime = 0x00004000,
        NonBlocking = 0x00010000
    }

    /// <summary>The FMOD_CREATESOUNDEXINFO layout through Core 2.04.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FmodCreateSoundInfo
    {
        internal int Size;
        internal uint Length;
        private uint _fileOffset;
        internal int Channels;
        internal int Frequency;
        internal FmodSampleFormat Format;
        private uint _decodeBufferSize;
        private int _initialSubsound;
        private int _subsoundCount;
        private IntPtr _inclusionList;
        private int _inclusionListCount;
        private IntPtr _pcmReadCallback;
        private IntPtr _pcmSetPositionCallback;
        private IntPtr _nonBlockingCallback;
        private IntPtr _dlsName;
        private IntPtr _encryptionKey;
        private int _maxPolyphony;
        private IntPtr _userData;
        private int _suggestedSoundType;
        private IntPtr _fileOpenCallback;
        private IntPtr _fileCloseCallback;
        private IntPtr _fileReadCallback;
        private IntPtr _fileSeekCallback;
        private IntPtr _fileAsyncReadCallback;
        private IntPtr _fileAsyncCancelCallback;
        private IntPtr _fileUserData;
        private int _fileBufferSize;
        private int _channelOrder;
        private IntPtr _initialSoundGroup;
        private uint _initialSeekPosition;
        private uint _initialSeekPositionType;
        private int _ignoreFileSystem;
        private uint _audioQueuePolicy;
        private uint _minMidiGranularity;
        private int _nonBlockingThreadId;
        private IntPtr _fsbGuid;
        internal int ResamplerMethod; // Present starting in Core 2.04.
    }

    /// <summary>Public FMOD Core C entry points in the Unity integration's Editor library.</summary>
    internal static class FmodNative
    {
        private const string Library = "fmodstudioL";
        internal const uint PcmFrames = 0x00000002;

        // FMOD_BOOL is a 32-bit integer. File names are UTF-8 on every Editor platform.
        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_Create(out IntPtr system, uint headerVersion);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_SetOutput(IntPtr system, FmodOutput output);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_GetOutput(IntPtr system, out FmodOutput output);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_GetNumDrivers(IntPtr system, out int drivers);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_Init(IntPtr system, int maxChannels, uint flags, IntPtr extraDriverData);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_Close(IntPtr system);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_Release(IntPtr system);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_Update(IntPtr system);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_MixerSuspend(IntPtr system);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_MixerResume(IntPtr system);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_CreateSound(IntPtr system,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, FmodMode mode, IntPtr info, out IntPtr sound);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_CreateSound(IntPtr system,
            byte[] data, FmodMode mode, ref FmodCreateSoundInfo info, out IntPtr sound);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_CreateStream(IntPtr system,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, FmodMode mode, IntPtr info, out IntPtr sound);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_System_PlaySound(IntPtr system, IntPtr sound, IntPtr channelGroup,
            int paused, out IntPtr channel);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_GetFormat(IntPtr sound, out int soundType,
            out FmodSampleFormat format, out int channels, out int bits);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_GetDefaults(IntPtr sound, out float frequency, out int priority);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_GetLength(IntPtr sound, out uint length, uint unit);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_GetOpenState(IntPtr sound, out FmodOpenState state,
            out uint percentBuffered, out int starving, out int diskBusy);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_ReadData(IntPtr sound, [Out] byte[] buffer, uint length, out uint read);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_SeekData(IntPtr sound, uint frame);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Sound_Release(IntPtr sound);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_SetMode(IntPtr channel, FmodMode mode);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_SetLoopPoints(IntPtr channel, uint start, uint startUnit,
            uint end, uint endUnit);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_SetLoopCount(IntPtr channel, int count);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_SetPosition(IntPtr channel, uint position, uint unit);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_GetPosition(IntPtr channel, out uint position, uint unit);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_SetPaused(IntPtr channel, int paused);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_IsPlaying(IntPtr channel, out int playing);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern FmodResult FMOD_Channel_Stop(IntPtr channel);
    }
}
