using ImpossibleRobert.Common;

namespace AudioTool
{
    /// <summary>Preserves the Audio Tools gapless parser surface for existing editor code.</summary>
    internal static class Mp3GaplessInfo
    {
        internal readonly struct Range
        {
            internal readonly int InfoFrameSamples;
            internal readonly int StartFrame;
            internal readonly int FrameCount;

            internal Range(EditorMp3GaplessInfo.Range range)
            {
                InfoFrameSamples = range.InfoFrameSamples;
                StartFrame = range.StartFrame;
                FrameCount = range.FrameCount;
            }
        }

        internal static float[] TrimDecodedSamples(string filePath, float[] samples, int channels) =>
            EditorMp3GaplessInfo.TrimDecodedSamples(filePath, samples, channels);

        internal static bool TryRead(string filePath, out Range range)
        {
            bool found = EditorMp3GaplessInfo.TryRead(filePath, out EditorMp3GaplessInfo.Range parsed);
            range = new Range(parsed);
            return found;
        }

        internal static bool TryRead(byte[] frame, int length, out Range range)
        {
            bool found = EditorMp3GaplessInfo.TryRead(frame, length, out EditorMp3GaplessInfo.Range parsed);
            range = new Range(parsed);
            return found;
        }
    }
}
