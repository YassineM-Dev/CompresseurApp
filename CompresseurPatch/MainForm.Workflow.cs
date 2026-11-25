using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace CompresseurPatch;

public sealed partial class MainForm
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".webm", ".flv", ".wmv",
        ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".3gp", ".ogv", ".gif",
    };

    private readonly CancellationTokenSource _lifetime = new();
    private DiscordRpc? _discord;

    private string? _pendingFile;
    private string? _inputFile;
    private string? _lastOutput;
    private VideoInfo? _video;
    private bool _busy;
    private CancellationTokenSource? _job;

    // ── Startup / wiring ────────────────────────────────────────────────────

    public void QueueInitialFile(string path) => _pendingFile = path;

    private void WireEvents()
    {
        Shown += async (_, _) =>
        {
            _discord = new DiscordRpc(AppendLog);
            await CheckFfmpegAsync();

            if (_pendingFile is not null)
            {
                var pending = _pendingFile;
                _pendingFile = null;
                await LoadVideoAsync(pending);
            }
        };

        FormClosed += (_, _) =>
        {
            try
            {
                _job?.Cancel();
                _lifetime.Cancel();
                _discord?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Nothing to do.
            }
        };

        _dropZone.AllowDrop = true;
        _dropZone.DragEnter += OnDragEnter;
        _dropZone.DragLeave += (_, _) => SetDropHover(false);
        _dropZone.DragDrop += OnDragDrop;
        _dropZone.MouseEnter += (_, _) => SetDropHover(_dropZone.AllowDrop);
        _dropZone.MouseLeave += (_, _) => SetDropHover(false);
        _dropZone.Click += OnBrowseClick;
        _dropTitle.MouseEnter += (_, _) => SetDropHover(_dropZone.AllowDrop);
        _dropHint.MouseEnter += (_, _) => SetDropHover(_dropZone.AllowDrop);
        _dropTitle.Click += OnBrowseClick;
        _dropHint.Click += OnBrowseClick;

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragLeave += (_, _) => SetDropHover(false);
        DragDrop += OnDragDrop;

        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => CancelJob();
        _openFolderButton.Click += (_, _) => OpenOutputFolder();

        _modeCompress.CheckedChanged += (_, _) => OnModeChanged();
        _modePatch60.CheckedChanged += (_, _) => OnModeChanged();
        _modePatch120.CheckedChanged += (_, _) => OnModeChanged();
        _crfBox.SelectedIndexChanged += (_, _) => UpdateOutputPreview();
    }

    private void ApplyInitialState()
    {
        _runButton.Enabled = false;
        _cancelButton.Enabled = false;
        _openFolderButton.Enabled = false;
        SetDropHover(false);
    }

    private async Task CheckFfmpegAsync()
    {
        if (!Ffmpeg.Locate(out _))
        {
            _infoLabel.ForeColor = Muted;
            _infoLabel.Text = "Preparing built-in ffmpeg engine (first run)…";
            SetStatus("Extracting built-in ffmpeg engine…");
        }

        if (await Ffmpeg.EnsureAvailableAsync(AppendLog, _lifetime.Token))
        {
            _infoLabel.ForeColor = Muted;
            _infoLabel.Text = "ffmpeg ready. Drop a video to start.";
            SetStatus("Ready.");
            AppendLog($"[OK] ffmpeg:  {Ffmpeg.FfmpegPath}");
            if (Ffmpeg.FfprobePath is not null)
            {
                AppendLog($"[OK] ffprobe: {Ffmpeg.FfprobePath}");
            }
            return;
        }

        _infoLabel.ForeColor = Danger;
        _infoLabel.Text = "ffmpeg could not be initialized.";
        AppendLog("[X] ffmpeg could not be initialized.");
        _runButton.Enabled = false;
    }

    // ── Mode selection ──────────────────────────────────────────────────────

    private CompressMode SelectedMode =>
        _modePatch120.Checked ? CompressMode.Patch120
        : _modePatch60.Checked ? CompressMode.Patch60
        : CompressMode.Compress;

    private void OnModeChanged()
    {
        var compress = SelectedMode == CompressMode.Compress;
        _crfBox.Enabled = compress;
        _runButton.Text = SelectedMode switch
        {
            CompressMode.Compress => "Compress video",
            CompressMode.Patch60 => "Patch to 60 fps",
            _ => "Patch to 120 fps",
        };

        UpdateOutputPreview();
    }

    private void UpdateOutputPreview()
    {
        if (_inputFile is null)
        {
            _outputLabel.Text = "Output: —";
            return;
        }

        var output = BuildOutputPath(_inputFile, SelectedMode);
        _outputLabel.Text = "Output: " + Path.GetFileName(output) + OutputNote();
    }

    private string OutputNote()
    {
        if (SelectedMode == CompressMode.Compress)
        {
            return "   (minterpolate + x264, CRF " + SelectedCrf() + ")";
        }

        if (_video is null)
        {
            return "   (itsscale, stream copy)";
        }

        var scale = PatchScale(SelectedMode);
        return $"   (itsscale {FormatScale(scale)} → real {TargetFps(SelectedMode):0} fps, {SpeedText(scale)})";
    }

    private static double TargetFps(CompressMode mode) => mode == CompressMode.Patch120 ? 120d : 60d;

    /// <summary>itscale shrinks timestamps: 30 fps source / 60 fps target = 0.5.</summary>
    private double PatchScale(CompressMode mode) =>
        _video is { Fps: > 0 } info ? info.Fps / TargetFps(mode) : 0.5;

    private static string SpeedText(double scale) =>
        scale > 0 ? $"{1 / scale:0.##}× playback speed" : "unknown speed";

    private static string FormatScale(double scale) => scale.ToString("0.#####", CultureInfo.InvariantCulture);

    private int SelectedCrf() =>
        int.TryParse(_crfBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var crf) ? crf : 18;

    private static string BuildOutputPath(string input, CompressMode mode)
    {
        var folder = Path.GetDirectoryName(input) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(input);

        var suffix = mode switch
        {
            CompressMode.Compress => "_Compressed",
            CompressMode.Patch60 => "_60fps",
            _ => "_120fps",
        };

        return Path.Combine(folder, name + suffix + ".mp4");
    }

    // ── Drag & drop / browse ────────────────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_busy || !TryGetDroppedFile(e, out _))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        e.Effect = DragDropEffects.Copy;
        SetDropHover(true);
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        SetDropHover(false);

        if (_busy || !TryGetDroppedFile(e, out var file))
        {
            return;
        }

        await LoadVideoAsync(file);
    }

    private static bool TryGetDroppedFile(DragEventArgs e, out string file)
    {
        file = string.Empty;

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return false;
        }

        var candidate = files[0];
        if (!File.Exists(candidate))
        {
            return false;
        }

        var extension = Path.GetExtension(candidate);
        if (!VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        file = candidate;
        return true;
    }

    private async void OnBrowseClick(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Pick a video to compress or patch",
            Filter = "Video files|*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.3gp;*.ogv|All files|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await LoadVideoAsync(dialog.FileName);
        }
    }

    private void SetDropHover(bool hover)
    {
        if (_dropHover == hover)
        {
            return;
        }

        _dropHover = hover;
        _dropZone.Highlighted = hover;
    }

    private async Task LoadVideoAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        if (!await Ffmpeg.EnsureAvailableAsync(AppendLog, _lifetime.Token))
        {
            SetStatus("ffmpeg could not be initialized.", error: true);
            return;
        }

        _inputFile = path;
        _video = null;
        _lastOutput = null;
        _runButton.Enabled = false;
        _openFolderButton.Enabled = false;
        _infoLabel.ForeColor = Muted;
        _infoLabel.Text = "Reading video info…";
        _progress.Value = 0;
        SetStatus("Reading video info…");

        AppendLog($"→ Input: {path}");

        try
        {
            _video = await Ffmpeg.ProbeAsync(path, _lifetime.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendLog("[X] ffprobe failed: " + ex.Message);
        }

        if (_video is null)
        {
            _inputFile = null;
            _dropTitle.Text = "Drop your video here";
            _dropHint.Text = "That file could not be read as a video - try another one.";
            _infoLabel.ForeColor = Danger;
            _infoLabel.Text = "This file is not a readable video.";
            SetStatus("Unreadable video file.", error: true);
            _outputLabel.Text = "Output: —";
            return;
        }

        _dropTitle.Text = Path.GetFileName(path);
        _dropHint.Text = "Click or drop another file to change it.";
        _infoLabel.ForeColor = Muted;
        _infoLabel.Text = _video.Describe();
        _runButton.Enabled = true;
        SetStatus("Ready. Pick a mode and hit the button.");

        AppendLog($"[OK] {_video.Width}x{_video.Height}  •  {_video.Fps:0.##} fps  •  {_video.Duration:hh\\:mm\\:ss}  •  {VideoInfo.FormatSize(_video.SizeBytes)}");
        _discord?.UpdateStatus(
            "Video Loaded • Ready",
            $"{_video.Width}x{_video.Height} • {_video.Fps:0.##} FPS ({VideoInfo.FormatSize(_video.SizeBytes)})",
            forceImmediate: true);
        UpdateOutputPreview();
    }

    private void UpdateDiscordProgress(double percent)
    {
        if (!_busy)
        {
            return;
        }

        var details = SelectedMode == CompressMode.Compress
            ? $"Compressing Video (CRF {SelectedCrf()})"
            : $"Patching Video to {TargetFps(SelectedMode):0} FPS";
        var state = _video is not null
            ? $"Working… {percent:0}% • {_video.Width}x{_video.Height}"
            : $"Working… {percent:0}%";

        _discord?.UpdateStatus(details, state);
    }

    // ── Processing ──────────────────────────────────────────────────────────

    private async Task RunAsync()
    {
        if (_busy || _inputFile is null || _video is null || Ffmpeg.FfmpegPath is null)
        {
            return;
        }

        var mode = SelectedMode;
        var input = _inputFile;
        var output = BuildOutputPath(input, mode);

        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("[X] Output path equals the input path - aborting.");
            return;
        }

        if (File.Exists(output))
        {
            AppendLog($"[!] Replacing existing output: {Path.GetFileName(output)}");
            try
            {
                File.Delete(output);
            }
            catch (Exception ex)
            {
                SetStatus("Cannot delete the old output file.", error: true);
                AppendLog("[X] Could not delete old output: " + ex.Message);
                return;
            }
        }

        _lastOutput = output;
        _busy = true;
        _job = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        SetBusy(true);
        _progress.Value = 0;

        AppendLog(string.Empty);

        try
        {
            var result = mode == CompressMode.Compress
                ? await RunCompressAsync(input, output, _video, _job.Token)
                : await RunPatchAsync(input, output, mode, _job.Token);

            if (_job.IsCancellationRequested)
            {
                AppendLog("[!] Cancelled. Partial file removed.");
                TryDelete(output);
                _progress.Value = 0;
                SetStatus("Cancelled.");
                _discord?.UpdateStatus("Compresseur/Patch", "Idle • Cancelled", forceImmediate: true);
                return;
            }

            if (!result.Succeeded || !File.Exists(output))
            {
                TryDelete(output);
                _progress.Value = 0;
                SetStatus("Failed - see the log above.", error: true);
                AppendLog("[X] ffmpeg exited with code " + result.ExitCode + ".");
                if (result.ErrorText.Length > 0)
                {
                    AppendLog("[X] Last error line: " + LastMeaningfulLine(result.ErrorText));
                }
                _discord?.UpdateStatus("Compresseur/Patch", "Idle • Error", forceImmediate: true);
                return;
            }

            _progress.Value = 100;
            ReportSuccess(input, output, mode);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[!] Cancelled.");
            TryDelete(output);
            _progress.Value = 0;
            SetStatus("Cancelled.");
            _discord?.UpdateStatus("Compresseur/Patch", "Idle • Cancelled", forceImmediate: true);
        }
        catch (Exception ex)
        {
            AppendLog("[X] " + ex.Message);
            SetStatus("Failed - " + ex.Message, error: true);
            _discord?.UpdateStatus("Compresseur/Patch", "Idle • Error", forceImmediate: true);
        }
        finally
        {
            _job?.Dispose();
            _job = null;
            _busy = false;
            SetBusy(false);
        }
    }

    private async Task<RunResult> RunCompressAsync(string input, string output, VideoInfo info, CancellationToken ct)
    {
        var crf = SelectedCrf();
        _discord?.UpdateStatus(
            $"Compressing Video (CRF {crf})",
            $"Starting… • {info.Width}x{info.Height} ({info.Fps:0.##} FPS)",
            forceImmediate: true);
        AppendLog($"[*] Compressing  •  {info.Width}x{info.Height}  •  {info.Fps:0.##} fps  •  CRF {crf}  •  preset veryslow");
        AppendLog("[*] Filter: minterpolate (mci/aobmc/bidir/vsbmc) + lanczos scale");

        if (info.Fps > 70)
        {
            AppendLog($"[!] {info.Fps:0} fps source - motion interpolation is slow per frame, expect a long run.");
        }

        AppendLog(string.Empty);

        var args = Ffmpeg.BuildCompressArgs(input, output, info, crf, reencodeAudio: false);
        var result = await Ffmpeg.RunAsync(args, info.Duration, AppendLog, SetProgress, ct);

        if (result.Succeeded || ct.IsCancellationRequested || result.ErrorText.Length == 0)
        {
            return result;
        }

        if (!result.ErrorText.Contains("audio", StringComparison.OrdinalIgnoreCase)
            && !result.ErrorText.Contains("codec", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        AppendLog(string.Empty);
        AppendLog("[!] Audio stream can't be copied into MP4 - retrying with AAC 192k…");
        AppendLog(string.Empty);
        TryDelete(output);

        var fallbackArgs = Ffmpeg.BuildCompressArgs(input, output, info, crf, reencodeAudio: true);
        return await Ffmpeg.RunAsync(fallbackArgs, info.Duration, AppendLog, SetProgress, ct);
    }

    private async Task<RunResult> RunPatchAsync(string input, string output, CompressMode mode, CancellationToken ct)
    {
        var target = TargetFps(mode);
        var scale = PatchScale(mode);
        _discord?.UpdateStatus(
            $"Patching Video to {target:0} FPS",
            $"Stream copy • {SpeedText(scale)}",
            forceImmediate: true);
        var expected = _video is null
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Max(0.5, _video.Duration.TotalSeconds * scale));

        AppendLog($"[*] Patching to {target:0} fps  •  itsscale {FormatScale(scale)}  •  stream copy (no re-encode)");
        AppendLog($"[*] Playback: {SpeedText(scale)}  •  new duration ≈ {expected:mm\\:ss}");
        AppendLog(string.Empty);

        var args = Ffmpeg.BuildPatchArgs(input, output, scale);
        return await Ffmpeg.RunAsync(args, expected, AppendLog, SetProgress, ct);
    }

    private void ReportSuccess(string input, string output, CompressMode mode)
    {
        var inputSize = new FileInfo(input).Length;
        var outputSize = new FileInfo(output).Length;

        AppendLog(string.Empty);
        AppendLog("============================================");
        AppendLog(mode == CompressMode.Compress ? "  [OK] COMPRESSION COMPLETE!" : "  [OK] PATCH COMPLETE!");
        AppendLog("============================================");
        AppendLog($"  Input:   {Path.GetFileName(input)}  ({VideoInfo.FormatSize(inputSize)})");
        AppendLog($"  Output:  {Path.GetFileName(output)}  ({VideoInfo.FormatSize(outputSize)})");

        if (mode == CompressMode.Compress && inputSize > 0)
        {
            var delta = (1 - outputSize / (double)inputSize) * 100;
            AppendLog(delta >= 0
                ? $"  Smaller: {delta:0.#}%  ({VideoInfo.FormatSize(inputSize - outputSize)} saved)"
                : $"  Bigger:  {-delta:0.#}%  (motion interpolation added data)");
        }

        if (mode != CompressMode.Compress && _video is not null)
        {
            var scale = PatchScale(mode);
            AppendLog($"  Now plays at {_video.Fps / scale:0.##} fps  (source {_video.Fps:0.##} fps, {SpeedText(scale)}).");
            AppendLog("  Upload it to TikTok.");
        }

        AppendLog("============================================");

        SetStatus($"Done in {Path.GetFileName(output)}  •  {VideoInfo.FormatSize(outputSize)}");
        _discord?.UpdateStatus(
            mode == CompressMode.Compress ? "Compression Complete!" : $"Patched to {TargetFps(mode):0} FPS!",
            $"Output: {VideoInfo.FormatSize(outputSize)} • Ready for TikTok",
            forceImmediate: true);
        _openFolderButton.Enabled = true;
    }

    private static string LastMeaningfulLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Length > 0)
            {
                return lines[i];
            }
        }

        return "(no details)";
    }

    private void CancelJob()
    {
        if (_job is null || _job.IsCancellationRequested)
        {
            return;
        }

        AppendLog("[!] Cancelling…");
        SetStatus("Cancelling…");
        _job.Cancel();
    }

    private void OpenOutputFolder()
    {
        var target = _lastOutput;
        if (target is null || !File.Exists(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("[X] Could not open the folder: " + ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        _runButton.Enabled = !busy && _inputFile is not null;
        _cancelButton.Enabled = busy;
        _openFolderButton.Enabled = !busy && _lastOutput is not null && File.Exists(_lastOutput);
        _modeCompress.Enabled = !busy;
        _modePatch60.Enabled = !busy;
        _modePatch120.Enabled = !busy;
        _crfBox.Enabled = !busy && SelectedMode == CompressMode.Compress;
        _dropZone.AllowDrop = !busy;

        UseWaitCursor = busy;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Locked file - nothing else we can do.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
