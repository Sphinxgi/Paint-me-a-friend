using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AudioTool
{
    internal enum AudioQueueStatus
    {
        Pending = 0,
        Analyzed = 1,
        Saved = 2,
        Failed = 3
    }

    /// <summary>One file of a multi-file editing session with its own selection and last result.</summary>
    [Serializable]
    internal sealed class AudioQueueEntry
    {
        public string Path;
        public string FileName;
        public string ProjectPath;
        public bool IsInProject;
        public string ExportFolder;
        public bool HasSelection;
        public float SelectionStart;
        public float SelectionEnd = 1f;
        public AudioQueueStatus Status;
        public string Summary;
        public string ResultKey;
        public string[] Outputs = new string[0];
        /// <summary>The original now holds its result; it is skipped by further Replace runs until the edits start over.</summary>
        public bool Replaced;

        internal bool IsProjectWav => IsInProject && !string.IsNullOrEmpty(ProjectPath) && ProjectPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);

        internal static AudioQueueEntry FromFile(string fullPath, string exportFolder = null)
        {
            FileAudioSource source = new FileAudioSource(fullPath);
            return new AudioQueueEntry
            {
                Path = fullPath,
                FileName = source.FileName,
                IsInProject = source.IsInProject,
                ProjectPath = source.ProjectPath,
                ExportFolder = string.IsNullOrEmpty(exportFolder) ? System.IO.Path.GetDirectoryName(fullPath) : exportFolder
            };
        }

        internal static AudioQueueEntry FromSource(IAudioSource source, string materializedPath, string exportFolder)
        {
            return new AudioQueueEntry
            {
                Path = materializedPath,
                FileName = source.FileName,
                IsInProject = source.IsInProject,
                ProjectPath = source.ProjectPath,
                ExportFolder = exportFolder
            };
        }

        internal IAudioSource CreateSource()
        {
            return new AudioQueueSource(this);
        }

        internal void ResetResult()
        {
            Status = AudioQueueStatus.Pending;
            Summary = null;
            ResultKey = null;
            Outputs = new string[0];
            Replaced = false;
        }
    }

    /// <summary>Files open in the Audio Editor. Kept outside the undoable session so navigating files never becomes an undo step.</summary>
    [Serializable]
    internal sealed class AudioFileQueue
    {
        public List<AudioQueueEntry> Entries = new List<AudioQueueEntry>();
        public int Index;
        /// <summary>Folder for batch copies; empty saves next to each original.</summary>
        public string DestinationFolder;

        internal bool IsMulti => Entries.Count > 1;
        internal AudioQueueEntry Current => Index >= 0 && Index < Entries.Count ? Entries[Index] : null;

        internal void Clear()
        {
            Entries.Clear();
            Index = 0;
        }

        internal int IndexOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            string full = Normalize(path);
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Normalize(Entries[i].Path), full, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        /// <summary>Adds the entry unless its file is already open.</summary>
        internal bool Add(AudioQueueEntry entry)
        {
            if (entry == null || IndexOf(entry.Path) >= 0) return false;
            Entries.Add(entry);
            return true;
        }

        internal void RemoveAt(int index)
        {
            if (index < 0 || index >= Entries.Count) return;
            Entries.RemoveAt(index);
            if (Index > index || Index >= Entries.Count) Index = Math.Max(0, Index - 1);
        }

        private static string Normalize(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path).Replace('\\', '/');
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return path;
            }
        }
    }

    /// <summary>Reopens a queued file with the identity it was added with, also for sources materialized by other tools.</summary>
    internal sealed class AudioQueueSource : IAudioSource
    {
        private readonly AudioQueueEntry _entry;

        internal AudioQueueSource(AudioQueueEntry entry)
        {
            _entry = entry;
        }

        public string FileName => _entry.FileName;
        public bool IsInProject => _entry.IsInProject;
        public string ProjectPath => _entry.ProjectPath;

        public Task<string> GetMaterializedPathAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_entry.Path);
        }
    }
}
