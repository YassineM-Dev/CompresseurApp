using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CompresseurPatch;

public enum CompressMode
{
    Compress,
    Patch60,
    Patch120,
}

public sealed record VideoInfo(int Width, int Height, double Fps, TimeSpan Duration, long SizeBytes)
{
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public string Describe() =>
        $"{Width}x{Height}   •   {Fps:0.##} fps   •   {Duration:hh\\:mm\\:ss}   •   {FormatSize(SizeBytes)}";
}

public sealed record RunResult(int ExitCode, string ErrorText)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Self-contained wrapper around ffmpeg / ffprobe. Automatically extracts the bundled
/// embedded ffmpeg.exe on machines that don't have FFmpeg installed, and probes videos
/// via ffmpeg -i when ffprobe is absent so the single EXE works out-of-the-box on any PC.
/// </summary>
public static class Ffmpeg
{
    public static string? FfmpegPath { get; private set; }
    public static string? FfprobePath { get; private set; }

    private static string BundledBinDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompresseurPatch", "bin");

    public static bool Locate(out string error)
    {
        error = string.Empty;
        FfmpegPath = Resolve("ffmpeg.exe");
        FfprobePath = Resolve("ffprobe.exe");

        if (FfmpegPath is not null)
        {
            return true;
        }

        error = "ffmpeg not found in PATH or bundled cache.";
        return false;
    }

    /// <summary>
    /// Ensures ffmpeg.exe is ready. If not installed on the system, extracts the bundled
    /// embedded ffmpeg.gz into %LOCALAPPDATA%\CompresseurPatch\bin\ffmpeg.exe.
    /// </summary>
    public static async Task<bool> EnsureAvailableAsync(Action<string>? log = null, CancellationToken ct = default)
    {
        if (Locate(out _))
        {
            return true;
        }

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var resource = asm.GetManifestResourceStream("CompresseurPatch.Assets.ffmpeg.gz");
            if (resource is null)
            {
                return false;
            }

            Directory.CreateDirectory(BundledBinDir);
            var targetPath = Path.Combine(BundledBinDir, "ffmpeg.exe");
            var tempPath = Path.Combine(BundledBinDir, $"ffmpeg.{Guid.NewGuid():N}.tmp");

            log?.Invoke("[*] First-run setup: extracting built-in ffmpeg engine…");

            await Task.Run(() =>
            {
                using var gzip = new GZipStream(resource, CompressionMode.Decompress);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                gzip.CopyTo(fileStream);
            }, ct).ConfigureAwait(false);

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            return Locate(out _);
        }
        catch (Exception ex)
        {
            log?.Invoke("[X] Built-in ffmpeg extraction failed: " + ex.Message);
            return false;
        }
    }

    private static string? Resolve(string fileName)
    {
        // 1. Beside the executable
        var besideApp = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(besideApp))
        {
            return besideApp;
        }

        // 2. Extracted bundled cache
        var cached = Path.Combine(BundledBinDir, fileName);
        if (File.Exists(cached) && new FileInfo(cached).Length > 1_000_000)
        {
            return cached;
        }

        // 3. System PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim().Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry - skip it.
            }
        }

        // 4. Common WinGet package directory fallback
        try
        {
            var wingetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetDir))
            {
                foreach (var pkg in Directory.EnumerateDirectories(wingetDir, "*FFmpeg*", SearchOption.TopDirectoryOnly))
                {
                    foreach (var found in Directory.EnumerateFiles(pkg, fileName, SearchOption.AllDirectories))
                    {
                        return found;
                    }
                }
            }
        }
        catch
        {
            // Ignore permission or directory errors.
        }

        return null;
    }

    public static async Task<VideoInfo?> ProbeAsync(string file, CancellationToken ct = default)
    {
        if (FfprobePath is not null)
        {
            var info = await ProbeWithFfprobeAsync(file, ct).ConfigureAwait(false);
            if (info is not null)
            {
                return info;
            }
        }

        if (FfmpegPath is not null)
        {
            return await ProbeWithFfmpegAsync(file, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<VideoInfo?> ProbeWithFfprobeAsync(string file, CancellationToken ct)
    {
        if (FfprobePath is null)
        {
            return null;
        }

        var psi = new ProcessStartInfo(FfprobePath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams");
        psi.ArgumentList.Add("v:0");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("stream=width,height,r_frame_rate:format=duration,size");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(file);

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        var json = await stdout.ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0 || json.Length == 0)
        {
            return null;
        }

        try
        {
            return ParseProbe(json, file);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<VideoInfo?> ProbeWithFfmpegAsync(string file, CancellationToken ct)
    {
        if (FfmpegPath is null)
        {
            return null;
        }

        var psi = new ProcessStartInfo(FfmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(file);

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return ParseFfmpegStderr(stderr, file);
    }

    private static VideoInfo? ParseFfmpegStderr(string stderr, string file)
    {
        if (string.IsNullOrWhiteSpace(stderr) || !stderr.Contains("Video:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int width = 0;
        int height = 0;
        double fps = 0;
        var duration = TimeSpan.Zero;

        var durMatch = Regex.Match(stderr, @"Duration:\s*(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)");
        if (durMatch.Success
            && int.TryParse(durMatch.Groups["h"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dh)
            && int.TryParse(durMatch.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dm)
            && double.TryParse(durMatch.Groups["s"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
        {
            duration = TimeSpan.FromHours(dh) + TimeSpan.FromMinutes(dm) + TimeSpan.FromSeconds(ds);
        }

        foreach (var line in stderr.Split('\n'))
        {
            if (!line.Contains("Video:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resMatch = Regex.Match(line, @"(?<=[,\s])(?<w>\d{2,5})x(?<h>\d{2,5})(?=[\s,\[])");
            if (resMatch.Success)
            {
                _ = int.TryParse(resMatch.Groups["w"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
                _ = int.TryParse(resMatch.Groups["h"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
            }

            var fpsMatch = Regex.Match(line, @"(?<fps>\d+(?:\.\d+)?)\s*fps\b");
            if (!fpsMatch.Success)
            {
                fpsMatch = Regex.Match(line, @"(?<fps>\d+(?:\.\d+)?)\s*tbr\b");
            }
            if (fpsMatch.Success)
            {
                _ = double.TryParse(fpsMatch.Groups["fps"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
            }

            break;
        }

        if (width <= 0) width = 1080;
        if (height <= 0) height = 1920;
        if (fps <= 0) fps = 60;

        long size = 0;
        try
        {
            if (File.Exists(file))
            {
                size = new FileInfo(file).Length;
            }
        }
        catch
        {
            // Ignore file info errors.
        }

        return new VideoInfo(width, height, fps, duration, size);
    }

    private static VideoInfo? ParseProbe(string json, string file)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
        {
            return null;
        }

        var stream = streams[0];
        var width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
        var height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

        double fps = 0;
        if (stream.TryGetProperty("r_frame_rate", out var rate) && rate.GetString() is { } fraction)
        {
            fps = ParseFraction(fraction);
        }

        var duration = TimeSpan.Zero;
        long size = 0;
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var d)
                && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            if (format.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var bytes))
            {
                size = bytes;
            }
        }

        if (size <= 0 && File.Exists(file))
        {
            size = new FileInfo(file).Length;
        }

        if (width <= 0) width = 1080;
        if (height <= 0) height = 1920;
        if (fps <= 0) fps = 60;

        return new VideoInfo(width, height, fps, duration, size);
    }

    private static double ParseFraction(string fraction)
    {
        var parts = fraction.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
            && den > 0)
        {
            return num / den;
        }

        return double.TryParse(fraction, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) ? single : 0;
    }

    /// <summary>Motion interpolation + lanczos scale, x264 CRF, veryslow.</summary>
    public static string BuildCompressArgs(string input, string output, VideoInfo info, int crf, bool reencodeAudio)
    {
        var fps = (int)Math.Max(1, Math.Round(info.Fps));
        var filter = $"minterpolate=fps={fps}:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1," +
                     $"scale={info.Width}:{info.Height}:flags=lanczos";
        var audio = reencodeAudio ? "-c:a aac -b:a 192k -ac 2" : "-c:a copy";

        return $"-hide_banner -nostats -progress pipe:1 -y -i {Quote(input)} " +
               $"-vf \"{filter}\" -r {fps} -c:v libx264 -preset veryslow -crf {crf} -pix_fmt yuv420p " +
               $"{audio} -movflags +faststart {Quote(output)}";
    }

    /// <summary>itsscale timestamp patch, pure stream copy (instant, no re-encode).</summary>
    public static string BuildPatchArgs(string input, string output, double scale)
    {
        var scaleText = scale.ToString("0.#####", CultureInfo.InvariantCulture);
        return $"-hide_banner -nostats -progress pipe:1 -y -itsscale {scaleText} -i {Quote(input)} " +
               $"-c copy -movflags +faststart {Quote(output)}";
    }

    public static async Task<RunResult> RunAsync(
        string arguments,
        TimeSpan expectedDuration,
        Action<string> log,
        Action<double> progress,
        CancellationToken ct)
    {
        if (FfmpegPath is null)
        {
            throw new InvalidOperationException("ffmpeg was not located.");
        }

        var psi = new ProcessStartInfo(FfmpegPath)
        {
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var stderr = new StringBuilder();
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                ReportProgress(e.Data, expectedDuration, progress);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }
            stderr.AppendLine(e.Data);
            log(e.Data);
        };
        process.Exited += (_, _) => completion.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start ffmpeg.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        });

        var exitCode = await completion.Task.ConfigureAwait(false);

        process.WaitForExit();

        return new RunResult(exitCode, stderr.ToString());
    }

    private static void ReportProgress(string line, TimeSpan expectedDuration, Action<double> progress)
    {
        if (line.StartsWith("progress=", StringComparison.Ordinal))
        {
            if (line.Contains("end", StringComparison.Ordinal))
            {
                progress(100);
            }
            return;
        }

        if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) || expectedDuration <= TimeSpan.Zero)
        {
            return;
        }

        if (!long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
            || microseconds <= 0)
        {
            return;
        }

        var percent = microseconds / 1_000_000.0 / expectedDuration.TotalSeconds * 100.0;
        progress(Math.Clamp(percent, 0, 100));
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", string.Empty) + "\"";
}
