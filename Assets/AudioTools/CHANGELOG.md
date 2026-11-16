# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-11-16

### Editing And Export

- Added multi-file editing: open several clips or folders with Assets/Edit Audio, drop multiple paths, or use Add Clips to build a queue. Files share processing settings while retaining individual selections. Analyze reports removed silence, gain, loudness and clipping without saving; cancellable batch saves write numbered copies beside each original or into one folder, or replace all WAV originals.
- Added Trim Silence as a non-destructive setting with kept lead-in and tail, plus Shorten pauses and Split into clips modes that save one numbered file per sound; the waveform marks saved clips, removed pauses and folded loop tails.
- Added loudness normalization (ITU-R BS.1770-4 integrated LUFS with absolute and relative gating, short clips measured ungated) with a peak ceiling, and a LOUDNESS readout.
- Added Seamless Loop with equal-power or linear crossfades folded into the start, Reverse, and Clean Up with DC offset removal and a 24 dB per octave high-pass. Region previews with Loop or shortened pauses play and loop the exact selected audio on both Unity and FMOD.
- Added an Output section with Match Source as the default, 16-bit PCM, 24-bit PCM and 32-bit float WAV, mono mixing or a single channel, and high-quality band-limited sample-rate conversion. Scripts can choose the output sample format through the new WavSampleFormat overload of AudioProcessor.ExportToWav.
- WAV exports preserve unedited 16-bit samples exactly and include the format information and padding needed for compatible 24-bit and float playback. Preview, single-clip saves and batch saves share selection, normalization, fade and volume processing, preserving the existing sound.
- Saved copies inherit the source asset's import settings, bundle assignment and user data. Replacing WAVs first keeps backups in Library/AudioTools/Backups, restored with Advanced > Restore Replaced Files.

### Playback And Reliability

- The Audio Editor automatically uses installed FMOD when Unity audio is disabled, covering playback, waveforms, processing, selection previews and WAV export, including Windows extended-length preview paths. When Unity settings block loading, the editor explains how to enable audio, opens Audio Settings and offers Retry instead of repeated decoding errors.
- MP3 files with LAME or FFmpeg gapless headers no longer gain leading or trailing silence in the Audio Editor or saved copies, keeping durations and sound onsets aligned with the encoded audio.
- Audio editing sessions survive repeated script recompiles and opening or creating scenes. Selection, effects and edits made during reload are preserved, Retry after a blocked reload keeps the session's settings, and controls show restored values while loading.
- The silence-threshold slider follows Undo and Reset Editing Session correctly, and decibel controls use readable 0.1 dB steps instead of clipped, overprecise values.
- Unrelated Undo or Redo no longer rebuilds and restarts the Audio Editor preview. WAV exports no longer add a Console message for every file, including internal preview files.

### Guidance

- Updated the included AI guidance for multi-file saves, output formats, silence modes, loudness, loops and clean-up, with the correct Save Copy, Import WAV and Save As WAV actions for each clip type and Selection only troubleshooting.
- Added a Rate on the Asset Store action to the welcome window and a one-time rating line after the third successful save in standalone installations.

## [1.2.0] - 2026-10-05

- Added AI guidance for setup, audio preview and selection, non-destructive processing and WAV export.
- Improved audio-preview recovery and editor cleanup on newer Unity versions while retaining Unity 2022.3 support.
- Simplified Tools and Project-window context-menu labels.

## [1.1.1] - 2026-08-07

- The welcome guide now opens once for standalone installations, stays quiet when bundled inside another Wetzold tool, and remains available from the Tools menu.

## [1.1.0] - 2026-07-30

- Rebuilt the Audio Editor as a responsive UI Toolkit workspace with waveform zooming, precise selection timing, keyboard controls and Undo. Automatic looping preview, outside-click deselection, and streamlined zoom, source and export controls keep editing responsive.
- Added drag-and-drop opening, a guided welcome experience, clearer empty and selection guidance, and improved silence selection controls while preserving the editor's warm audio-focused identity.
- Added clearer source, processing, clipping and export feedback with safer WAV replacement.
- Reduced scripting clutter by keeping the audio editor, menus and context-menu implementation internal.
- Requires Unity 2022.3 or newer.

## [1.0.0] - 2026-01-23

- Initial release of Audio Tools for selecting and trimming audio, automatically detecting silence, and applying curve-based fades, normalization and volume changes.
- Save edited audio as .wav.
