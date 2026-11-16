using System;
using System.IO;
using System.Runtime.InteropServices;
using ImpossibleRobert.Common;

namespace ImpossibleRobert.Common.Integration.FMOD
{
    internal readonly struct FmodSystem
    {
        internal FmodSystem(IntPtr handle, uint headerVersion)
        {
            Handle = handle;
            HeaderVersion = headerVersion;
        }

        internal IntPtr Handle { get; }
        internal uint HeaderVersion { get; }
        internal bool IsValid => Handle != IntPtr.Zero;
    }

    /// <summary>Editor audio operations over FMOD's public Core C API.</summary>
    internal static class FmodApi
    {
        private const uint OldestHeader = 0x00020200;
        private const uint HeaderWithResampler = 0x00020400;
        private const uint LatestHeader = HeaderWithResampler;
        private const uint MinorVersionStep = 0x00000100;

        internal static FmodSystem CreateSystem(bool silent)
        {
            FmodSystem system = CreateCoreSystem();
            bool initialized = false;
            try
            {
                if (silent)
                    Check(FmodNative.FMOD_System_SetOutput(system.Handle, FmodOutput.NoSound), "offline output selection", true);
                Check(FmodNative.FMOD_System_Init(system.Handle, 32, 0, IntPtr.Zero), "system initialization", true);
                initialized = true;

                if (!silent)
                {
                    Check(FmodNative.FMOD_System_GetOutput(system.Handle, out FmodOutput output), "output query", true);
                    if (output >= FmodOutput.Autodetect && output <= FmodOutput.WaveWriterNonRealtime)
                        throw new EditorAudioUnavailableException("FMOD has no audible output (" + output + "). Check the audio device, then retry.");
                    Check(FmodNative.FMOD_System_GetNumDrivers(system.Handle, out int drivers), "output driver query", true);
                    if (drivers < 1)
                        throw new EditorAudioUnavailableException("FMOD found no audio output device. Connect an output device, then retry.");
                }
                return system;
            }
            catch
            {
                ReleaseSystem(system, initialized);
                throw;
            }
        }

        private static FmodSystem CreateCoreSystem()
        {
            try
            {
                // System_Create requires the Core library's major and minor version.
                for (uint header = LatestHeader; header >= OldestHeader; header -= MinorVersionStep)
                {
                    FmodResult result = FmodNative.FMOD_System_Create(out IntPtr system, header);
                    if (result == FmodResult.ERR_HEADER_MISMATCH) continue;
                    Check(result, "system creation", true);
                    return new FmodSystem(system, header);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException ||
                exception is EntryPointNotFoundException || exception is BadImageFormatException)
            {
                throw new EditorAudioUnavailableException("FMOD's native Editor library could not be loaded. Check the FMOD for Unity installation, then retry.", exception);
            }

            throw new EditorAudioUnavailableException("The FMOD editor audio bridge supports FMOD Core 2.02 through 2.04. Check the FMOD for Unity version, then retry.");
        }

        internal static void ReleaseSystem(FmodSystem system, bool initialized = true, bool suspended = false)
        {
            if (!system.IsValid) return;
            try
            {
                if (initialized)
                {
                    if (suspended) Resume(system);
                    FmodNative.FMOD_System_Close(system.Handle);
                }
            }
            finally
            {
                FmodNative.FMOD_System_Release(system.Handle);
            }
        }

        internal static void Update(FmodSystem system)
        {
            Check(FmodNative.FMOD_System_Update(system.Handle), "system update");
        }

        internal static void Suspend(FmodSystem system)
        {
            Check(FmodNative.FMOD_System_MixerSuspend(system.Handle), "mixer suspend");
        }

        internal static void Resume(FmodSystem system)
        {
            Check(FmodNative.FMOD_System_MixerResume(system.Handle), "mixer resume");
        }

        internal static IntPtr OpenFile(FmodSystem system, string path, bool streaming)
        {
            IntPtr sound;
            FmodResult result = streaming
                ? FmodNative.FMOD_System_CreateStream(system.Handle, path,
                    FmodMode.TwoDimensional | FmodMode.NonBlocking | FmodMode.LoopOff, IntPtr.Zero, out sound)
                : FmodNative.FMOD_System_CreateSound(system.Handle, path,
                    FmodMode.TwoDimensional | FmodMode.OpenOnly | FmodMode.AccurateTime, IntPtr.Zero, out sound);
            if (result != FmodResult.OK) Check(result, "opening " + Path.GetFileName(path));
            return sound;
        }

        internal static IntPtr OpenFloatSamples(FmodSystem system, byte[] data, int channels, int frequency)
        {
            int infoSize = system.HeaderVersion >= HeaderWithResampler
                ? Marshal.SizeOf<FmodCreateSoundInfo>()
                : Marshal.OffsetOf<FmodCreateSoundInfo>(nameof(FmodCreateSoundInfo.ResamplerMethod)).ToInt32();
            FmodCreateSoundInfo info = new FmodCreateSoundInfo
            {
                Size = infoSize,
                Length = checked((uint)data.Length),
                Channels = channels,
                Frequency = frequency,
                Format = FmodSampleFormat.PcmFloat
            };
            // FMOD copies the PCM during this synchronous OPENMEMORY call.
            FmodMode mode = FmodMode.TwoDimensional | FmodMode.OpenMemory | FmodMode.OpenRaw | FmodMode.CreateSample;
            Check(FmodNative.FMOD_System_CreateSound(system.Handle, data, mode, ref info, out IntPtr sound), "creating processed audio");
            return sound;
        }

        internal static EditorAudioMetadata Metadata(IntPtr sound, out FmodSampleFormat format)
        {
            Check(FmodNative.FMOD_Sound_GetFormat(sound, out _, out format, out int channels, out _), "format query");
            Check(FmodNative.FMOD_Sound_GetDefaults(sound, out float rate, out _), "sample rate query");
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 1 || rate > int.MaxValue)
                throw new InvalidDataException("FMOD returned an invalid audio sample rate.");
            Check(FmodNative.FMOD_Sound_GetLength(sound, out uint length, FmodNative.PcmFrames), "length query");
            return new EditorAudioMetadata(channels, checked((int)Math.Round(rate)), length);
        }

        internal static FmodOpenState OpenState(IntPtr sound, string path)
        {
            FmodResult result = FmodNative.FMOD_Sound_GetOpenState(sound, out FmodOpenState state, out _, out _, out _);
            if (result != FmodResult.OK) Check(result, "opening " + Path.GetFileName(path));
            return state;
        }

        internal static FmodResult Read(IntPtr sound, byte[] buffer, out uint read)
        {
            FmodResult result = FmodNative.FMOD_Sound_ReadData(sound, buffer, (uint)buffer.Length, out read);
            if (result != FmodResult.ERR_FILE_EOF) Check(result, "decoding audio");
            return result;
        }

        internal static void SeekData(IntPtr sound, uint frame)
        {
            Check(FmodNative.FMOD_Sound_SeekData(sound, frame), "seeking decoded audio");
        }

        internal static void ReleaseSound(IntPtr sound)
        {
            if (sound != IntPtr.Zero) FmodNative.FMOD_Sound_Release(sound);
        }

        internal static IntPtr Play(FmodSystem system, IntPtr sound, long startFrame, long endFrameExclusive, bool loop)
        {
            Check(FmodNative.FMOD_System_PlaySound(system.Handle, sound, IntPtr.Zero, 1, out IntPtr channel), "starting playback");
            try
            {
                FmodMode mode = FmodMode.TwoDimensional | (loop ? FmodMode.LoopNormal : FmodMode.LoopOff);
                Check(FmodNative.FMOD_Channel_SetMode(channel, mode), "loop mode");
                if (loop)
                {
                    Check(FmodNative.FMOD_Channel_SetLoopPoints(channel, checked((uint)startFrame), FmodNative.PcmFrames,
                        checked((uint)(endFrameExclusive - 1)), FmodNative.PcmFrames), "loop region");
                    Check(FmodNative.FMOD_Channel_SetLoopCount(channel, -1), "loop count");
                }
                if (startFrame > 0) Seek(channel, startFrame);
                Pause(channel, false);
                return channel;
            }
            catch
            {
                Stop(channel);
                throw;
            }
        }

        internal static long Position(IntPtr channel)
        {
            FmodResult result = FmodNative.FMOD_Channel_GetPosition(channel, out uint position, FmodNative.PcmFrames);
            return result == FmodResult.OK ? position : 0;
        }

        internal static void Seek(IntPtr channel, long frame)
        {
            Check(FmodNative.FMOD_Channel_SetPosition(channel, checked((uint)frame), FmodNative.PcmFrames), "seeking playback");
        }

        internal static bool IsPlaying(IntPtr channel)
        {
            FmodResult result = FmodNative.FMOD_Channel_IsPlaying(channel, out int playing);
            return result == FmodResult.OK && playing != 0;
        }

        internal static void Stop(IntPtr channel)
        {
            if (channel != IntPtr.Zero) FmodNative.FMOD_Channel_Stop(channel);
        }

        internal static void Pause(IntPtr channel, bool paused)
        {
            Check(FmodNative.FMOD_Channel_SetPaused(channel, paused ? 1 : 0),
                paused ? "pausing playback" : "resuming playback");
        }

        private static void Check(FmodResult result, string operation, bool availability = false)
        {
            if (result == FmodResult.OK) return;
            string message = "FMOD " + operation + " failed (" + result + ").";
            if (availability) throw new EditorAudioUnavailableException(message + " Check the FMOD installation or audio output, then retry.");
            throw new InvalidDataException(message);
        }
    }
}
