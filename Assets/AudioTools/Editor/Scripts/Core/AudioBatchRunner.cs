using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace AudioTool
{
    internal sealed class AudioBatchOptions
    {
        internal AudioOutputFormat Format;
        /// <summary>Overwrites WAV assets in place (after a backup) instead of writing numbered copies.</summary>
        internal bool ReplaceOriginals;
        /// <summary>Folder for copies; null writes next to each original.</summary>
        internal string DestinationFolder;
        internal bool CopyImportSettings = true;
    }

    internal sealed class AudioBatchReport
    {
        internal int Saved;
        internal int Analyzed;
        internal int Failed;
        internal bool Cancelled;
        internal string BackupFolder;
        internal readonly List<string> Written = new List<string>();
    }

    /// <summary>Applies one set of edit settings to many files: analyze without writing, or save copies or replacements.</summary>
    internal static class AudioBatchRunner
    {
        internal static string CreateResultKey(AudioEditSettings settings, AudioBatchOptions options, bool write)
        {
            return settings.CreateKey() + (write ? "W" : "A") + (int)options.Format + (options.ReplaceOriginals ? "R" : "C") + (options.DestinationFolder ?? string.Empty);
        }

        /// <param name="entries">Files to process; each keeps its own selection.</param>
        /// <param name="settings">Shared settings; the selection of each entry replaces the selection in them.</param>
        /// <param name="options">Output choices.</param>
        /// <param name="write">False analyzes only.</param>
        /// <param name="progress">Receives the index of the file being processed, the total and its name.</param>
        /// <param name="token">Stops after the current file.</param>
        internal static async Task<AudioBatchReport> RunAsync(IReadOnlyList<AudioQueueEntry> entries, AudioEditSettings settings, AudioBatchOptions options, bool write,
            Action<int, int, string> progress, CancellationToken token)
        {
            AudioBatchReport report = new AudioBatchReport();
            List<AudioImportRequest> imports = new List<AudioImportRequest>();
            using (CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                int progressId = Progress.Start(write ? "Saving audio" : "Analyzing audio", null, Progress.Options.None);
                Progress.RegisterCancelCallback(progressId, () =>
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (ObjectDisposedException) { }

                    return true;
                });
                try
                {
                    if (write && options.ReplaceOriginals)
                    {
                        List<string> originals = new List<string>();
                        foreach (AudioQueueEntry entry in entries)
                        {
                            if (entry.IsProjectWav) originals.Add(entry.Path);
                        }

                        if (originals.Count > 0) report.BackupFolder = AudioReplaceBackup.Create(originals);
                    }

                    for (int i = 0; i < entries.Count; i++)
                    {
                        AudioQueueEntry entry = entries[i];
                        progress?.Invoke(i, entries.Count, entry.FileName);
                        Progress.Report(progressId, i, entries.Count, entry.FileName);
                        try
                        {
                            await ProcessAsync(entry, settings, options, write, imports, report, cancellation.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            report.Cancelled = true;
                            break;
                        }
                        catch (Exception exception)
                        {
                            entry.Status = AudioQueueStatus.Failed;
                            entry.Summary = exception is AudioDecodeException ? exception.Message : "Failed: " + exception.Message;
                            entry.ResultKey = null;
                            report.Failed++;
                        }
                    }
                }
                finally
                {
                    Progress.Finish(progressId, report.Cancelled ? Progress.Status.Canceled : report.Failed > 0 ? Progress.Status.Failed : Progress.Status.Succeeded);
                    if (imports.Count > 0) AudioOutputWriter.Import(imports, options.CopyImportSettings);
                }
            }

            progress?.Invoke(entries.Count, entries.Count, null);
            return report;
        }

        private static async Task ProcessAsync(AudioQueueEntry entry, AudioEditSettings settings, AudioBatchOptions options, bool write,
            List<AudioImportRequest> imports, AudioBatchReport report, CancellationToken token)
        {
            AudioEditSettings entrySettings = settings.WithSelection(entry.HasSelection, entry.SelectionStart, entry.SelectionEnd);
            AudioDecodedSource decoded = await AudioSourceDecoder.DecodeAsync(entry.Path, false, null, token);
            int channels = decoded.Metadata.Channels;
            int frequency = decoded.Metadata.Frequency;
            float[] samples = decoded.Samples;
            AudioRenderResult render = await Task.Run(() => AudioEditPipeline.Render(samples, channels, frequency, entrySettings, token), token);
            List<AudioOutputClip> outputs = await Task.Run(() => AudioEditPipeline.RenderOutputs(render, entrySettings, token), token);
            token.ThrowIfCancellationRequested();
            entry.Summary = Summarize(render, outputs.Count);
            if (!write)
            {
                entry.Status = AudioQueueStatus.Analyzed;
                entry.ResultKey = CreateResultKey(entrySettings, options, false);
                report.Analyzed++;
                return;
            }

            if (outputs.Count == 0) throw new InvalidOperationException("The selected region contains no audio samples.");
            WavSampleFormat format = AudioWavWriter.Resolve(options.Format, entry.Path);
            List<string> written;
            if (options.ReplaceOriginals && entry.IsProjectWav && outputs.Count == 1)
            {
                AudioWavWriter.Write(entry.Path, outputs[0].Samples, outputs[0].Channels, outputs[0].Frequency, format);
                entry.Replaced = true;
                written = new List<string> { entry.Path };
            }
            else
            {
                string folder = string.IsNullOrEmpty(options.DestinationFolder) ? entry.ExportFolder : options.DestinationFolder;
                written = AudioOutputWriter.Write(outputs, folder, entry.FileName, null, format);
            }

            foreach (string path in written)
            {
                imports.Add(new AudioImportRequest(path, entry.IsInProject ? entry.ProjectPath : null));
                report.Written.Add(path);
            }

            entry.Outputs = written.ToArray();
            entry.Status = AudioQueueStatus.Saved;
            entry.ResultKey = CreateResultKey(entrySettings, options, true);
            report.Saved++;
        }

        /// <summary>Short result line: removed silence, clip count, gain, loudness and clipping.</summary>
        internal static string Summarize(AudioRenderResult render, int outputCount)
        {
            StringBuilder text = new StringBuilder();
            if (render.NoAudibleContent)
            {
                text.Append("No audible content");
            }
            else if (render.Segments.Count > 0)
            {
                double lead = (double)(render.Segments[0].Start - render.RegionStart) / render.Frequency;
                double tail = (double)(render.RegionEnd - render.Segments[render.Segments.Count - 1].End) / render.Frequency;
                if (lead > 0 || tail > 0) Append(text, Removed(lead) + " / " + Removed(tail));
            }

            if (outputCount > 1) Append(text, outputCount.ToString(CultureInfo.InvariantCulture) + " clips");
            if (render.Segments.Count == 1)
            {
                AudioRenderSegment segment = render.Segments[0];
                if (Math.Abs(segment.Gain - 1f) > 0.0001f) Append(text, Decibels(segment.Gain) + " dB");
                if (!segment.Loudness.IsSilent) Append(text, segment.Loudness.Integrated.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS");
                if (segment.LimitedByCeiling) Append(text, "held at ceiling");
            }
            else if (render.AnyLimitedByCeiling)
            {
                Append(text, "some held at ceiling");
            }

            if (render.ClippedSampleCount > 0) Append(text, render.ClippedSampleCount.ToString(CultureInfo.InvariantCulture) + " clipped");
            return text.Length == 0 ? "Unchanged" : text.ToString();
        }

        private static void Append(StringBuilder text, string part)
        {
            if (text.Length > 0) text.Append(" · ");
            text.Append(part);
        }

        private static string Removed(double seconds)
        {
            return seconds > 0 ? "-" + Seconds(seconds) : "0 s";
        }

        private static string Seconds(double seconds)
        {
            return seconds.ToString("0.000", CultureInfo.InvariantCulture) + " s";
        }

        private static string Decibels(float gain)
        {
            double decibels = 20.0 * Math.Log10(Math.Max(1e-6, gain));
            return (decibels >= 0 ? "+" : string.Empty) + decibels.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
