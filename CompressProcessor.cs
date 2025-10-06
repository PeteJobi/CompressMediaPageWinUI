using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CompressMediaPage
{
    public class CompressProcessor
    {
        private string ffmpegPath;
        private string mediaPath;
        private Process? currentProcess;
        private bool hasBeenKilled;
        private const string FileNameLongError =
            "The source file name is too long. Shorten it to get the total number of characters in the destination directory lower than 256.\n\nDestination directory: ";

        public CompressProcessor(string ffmpegPath, string mediaPath)
        {
            this.ffmpegPath = ffmpegPath;
            this.mediaPath = mediaPath;
        }

        public async Task<VideoDetails> GetVideoDetails(Action<string> error)
        {
            var size = new FileInfo(mediaPath).Length / (1024.0 * 1024.0); // Size in MB
            double bitrate = 0, fps = 0;
            int width = 0, height = 0;
            var valuesSet = false;

            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (CheckFileNameLongError(args.Data, error)) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream #\d+:\d+.*?: Video: .+?, (\d+)x(\d+).*?,(?: (\d+) kb/s,)? (\d+?\.?\d*?) fps");
                if (matchCollection.Count == 0) return;
                width = int.Parse(matchCollection[0].Groups[1].Value);
                height = int.Parse(matchCollection[0].Groups[2].Value);
                _ = double.TryParse(matchCollection[0].Groups[3].Value, out bitrate);
                fps = double.Parse(matchCollection[0].Groups[4].Value);
                valuesSet = true;
            });

            return new VideoDetails
            {
                Size = size,
                Bitrate = bitrate,
                Resolution = new Size(width, height),
                Fps = fps
            };
        }

        public async Task<AudioDetails> GetAudioDetails(Action<string> error)
        {
            var size = new FileInfo(mediaPath).Length / (1024.0 * 1024.0); // Size in MB
            int bitrate = 0, sampleRate = 0;
            var valuesSet = false;

            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (CheckFileNameLongError(args.Data, error)) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream #\d+:\d+.*?: Audio: .+?, (\d+) Hz.+?, (\d+) kb/s");
                if (matchCollection.Count == 0) return;
                sampleRate = int.Parse(matchCollection[0].Groups[1].Value);
                bitrate = int.Parse(matchCollection[0].Groups[2].Value);
                valuesSet = true;
            });

            return new AudioDetails
            {
                Size = size,
                AudioRate = sampleRate,
                Bitrate = bitrate
            };
        }

        public async Task<Size> GetImageResolution(Action<string> error)
        {
            int width = 0, height = 0;
            var valuesSet = false;
            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (CheckFileNameLongError(args.Data, error)) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream #\d+:\d+.*?: Video: .+?, (\d+)x(\d+)");
                if (matchCollection.Count == 0) return;
                width = int.Parse(matchCollection[0].Groups[1].Value);
                height = int.Parse(matchCollection[0].Groups[2].Value);
                valuesSet = true;
            });

            return new Size(width, height);
        }

        public async Task CompressResolution(int width, bool isImage, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            var outputFile = GetOutputName(mediaPath, setOutputFile);
            var codec = isImage ? "" : "-c:v libx265 -c:a copy -crf 18";
            await StartProcess($"-i \"{mediaPath}\" -vf \"scale={width}:-1\" {codec} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (!isImage && CheckX265Error(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (args.Data.StartsWith("frame"))
                {
                    if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressFPS(double fps, bool isGif, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            var outputFile = GetOutputName(mediaPath, setOutputFile);
            var codec = isGif ? "" : "-c:v libx265 -c:a copy -crf 18";
            await StartProcess($"-i \"{mediaPath}\" -r {fps} {codec} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (!isGif && CheckX265Error(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (args.Data.StartsWith("frame"))
                {
                    if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressSize(double sizeInMb, bool limitToTarget, bool isAudio, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            var parsedAudioBitrate = 0;
            progress.Report(new ValueProgress(0, "0.0 %"));
            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (parsedAudioBitrate == 0)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Stream .+: Audio: .+ (\d+) kb/s.+");
                    if (matchCollection.Count == 0) return;
                    parsedAudioBitrate = int.Parse(matchCollection[0].Groups[1].Value);
                }
            });

            var totalBitrate = sizeInMb * 1000 * 8 / duration.TotalSeconds; // in bits per second
            if (!isAudio)
            {
                var audioBitrate = parsedAudioBitrate;
                totalBitrate -= audioBitrate;
            }
            await CompressBitrate(totalBitrate, limitToTarget, isAudio, progressMax, progress, setOutputFile, error);
        }

        public async Task CompressBitrate(double bitrate, bool limitToTarget, bool isAudio, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
            });

            bitrate *= 1000;
            var command = isAudio ? $"-b:a {bitrate}" : $"-b:v {bitrate} -c:v libx265 -c:a copy";
            var limitToTargetCommand = limitToTarget ? $"-maxrate:v {bitrate} -bufsize:v {bitrate}" : string.Empty;

            var outputFile = GetOutputName(mediaPath, setOutputFile);
            var progressRegexStart = isAudio ? @"^size=\s*\d+kB.+?" : @"^frame=\s*\d+\s.+?";
            await StartProcess($"-i \"{mediaPath}\" {command} {limitToTargetCommand} \"{outputFile}\"", null, (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (CheckFileNameLongError(args.Data, error)) return;
                if (!isAudio && CheckX265Error(args.Data, error)) return;
                if (!args.Data.StartsWith("size") && !args.Data.StartsWith("frame")) return;
                if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                MatchCollection matchCollection = Regex.Matches(args.Data, progressRegexStart + @"time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                if (matchCollection.Count == 0) return;
                IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressCRF(int crf, string preset, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            var outputFile = GetOutputName(mediaPath, setOutputFile);
            await StartProcess($"-i \"{mediaPath}\" -c:v libx265 -c:a copy -preset {preset} -crf {crf} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (CheckX265Error(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (args.Data.StartsWith("frame"))
                {
                    if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressAudioQualityFactor(int qa, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            var outputFile = GetOutputName(mediaPath, setOutputFile);
            await StartProcess($"-i \"{mediaPath}\" -c:a libmp3lame -q:a {qa} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (args.Data.StartsWith("size"))
                {
                    if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^size=\s*\d+kB.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressAudioSamplingRate(double ar, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var parsedAudioBitrate = 0;
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            await StartProcess($"-i \"{mediaPath}\"", null, (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (parsedAudioBitrate == 0)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Stream .+: Audio: .+ (\d+) kb/s");
                    if (matchCollection.Count == 0) return;
                    parsedAudioBitrate = int.Parse(matchCollection[0].Groups[1].Value);
                }
            });

            var outputFile = GetOutputName(mediaPath, setOutputFile);
            await StartProcess($"-i \"{mediaPath}\" -ar {ar} -b:a {parsedAudioBitrate * 1000} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (!args.Data.StartsWith("size")) return;
                if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                var matchCollection = Regex.Matches(args.Data, @"^size=\s*\d+kB.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                if (matchCollection.Count == 0) return;
                IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
        }

        public async Task CompressImageQualityFactor(int qv, double progressMax, IProgress<ValueProgress> progress, Action<string> setOutputFile, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            progress.Report(new ValueProgress(0, "0.0 %"));
            var outputFile = GetOutputName(mediaPath, setOutputFile);
            await StartProcess($"-i \"{mediaPath}\" -q:v {qv} \"{outputFile}\"", null, (o, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (CheckFileNameLongError(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (args.Data.StartsWith("size"))
                {
                    if (CheckNoSpaceDuringOperation(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^size=\s*\d+kB.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, progressMax, progress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(progressMax, progress);
            
        }

        public static MediaType GetMediaType(string path)
        {
            var extension = Path.GetExtension(path).ToLower();
            return extension switch
            {
                ".mp4" or ".mkv" or ".avi" or ".mov" => MediaType.Video,
                ".mp3" or ".wav"/* or ".aac" or ".flac"*/ => MediaType.Audio,
                ".jpg" or ".jpeg" => MediaType.ImageJpg,
                ".png" => MediaType.ImagePng,
                ".gif" => MediaType.ImageGif,
                _ => throw new NotSupportedException($"Unsupported media type: {extension}"),
            };
        }

        public static string GetFileName(string path) => Path.GetFileName(path);

        private static string GetOutputName(string path, Action<string> setFile)
        {
            var inputName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var parentFolder = Path.GetDirectoryName(path) ?? throw new FileNotFoundException($"The specified path does not exist: {path}");
            var outputFile = Path.Combine(parentFolder, $"{inputName}_COMPRESSED{extension}");
            setFile(outputFile);
            File.Delete(outputFile);
            return outputFile;
        }

        private bool CheckNoSpaceDuringOperation(string line, Action<string> error)
        {
            if (!line.EndsWith("No space left on device") && !line.EndsWith("I/O error")) return false;
            SuspendProcess(currentProcess);
            error($"Process failed.\nError message: {line}");
            return true;
        }

        private static bool CheckFileNameLongError(string line, Action<string> error)
        {
            const string noSuchDirectory = ": No such file or directory";
            if (!line.EndsWith(noSuchDirectory)) return false;
            error(FileNameLongError + line[..^noSuchDirectory.Length]);
            return true;
        }

        private static bool CheckX265Error(string line, Action<string> error)
        {
            const string x265Error = "x265 [error]: ";
            if (!line.StartsWith(x265Error)) return false;
            error(line[x265Error.Length..]);
            return true;
        }

        private void IncrementProgress(TimeSpan currentTime, TimeSpan totalDuration, double max, IProgress<ValueProgress> progress)
        {
            var fraction = currentTime / totalDuration;
            progress.Report(new ValueProgress(fraction * max, $"{Math.Round(fraction * 100, 2)} %"));
        }

        void AllDone(double max, IProgress<ValueProgress> valueProgress)
        {
            currentProcess = null;
            valueProgress.Report(new ValueProgress
            {
                ActionProgress = max,
                ActionProgressText = "100 %"
            });
        }

        public void ViewFiles(string file)
        {
            var info = new ProcessStartInfo();
            info.FileName = "explorer";
            info.Arguments = $"/e, /select, \"{file}\"";
            Process.Start(info);
        }

        bool HasBeenKilled()
        {
            if (!hasBeenKilled) return false;
            hasBeenKilled = false;
            return true;
        }

        private static void SuspendProcess(Process process)
        {
            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                SuspendThread(pOpenThread);

                CloseHandle(pOpenThread);
            }
        }

        public async Task Cancel(string? outputFile)
        {
            if (currentProcess == null) return;
            currentProcess.Kill();
            await currentProcess.WaitForExitAsync();
            hasBeenKilled = true;
            currentProcess = null;
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }

        public void Pause()
        {
            if (currentProcess == null) return;
            SuspendProcess(currentProcess);
        }

        public void Resume()
        {
            if (currentProcess == null) return;
            if (currentProcess.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in currentProcess.Threads)
            {
                var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                int suspendCount;
                do
                {
                    suspendCount = ResumeThread(pOpenThread);
                } while (suspendCount > 0);

                CloseHandle(pOpenThread);
            }
        }

        async Task StartProcess(string arguments, DataReceivedEventHandler? outputEventHandler, DataReceivedEventHandler? errorEventHandler)
        {
            Process ffmpeg = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
                EnableRaisingEvents = true
            };
            ffmpeg.OutputDataReceived += outputEventHandler;
            ffmpeg.ErrorDataReceived += errorEventHandler;
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();
            ffmpeg.BeginOutputReadLine();
            currentProcess = ffmpeg;
            await ffmpeg.WaitForExitAsync();
            ffmpeg.Dispose();
            currentProcess = null;
        }

        [Flags]
        public enum ThreadAccess : int
        {
            SUSPEND_RESUME = (0x0002)
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        [DllImport("kernel32.dll")]
        static extern uint SuspendThread(IntPtr hThread);
        [DllImport("kernel32.dll")]
        static extern int ResumeThread(IntPtr hThread);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }

    public struct VideoDetails
    {
        public double Size { get; set; }
        public double Bitrate { get; set; }
        public Size Resolution { get; set; }
        public double Fps { get; set; }
    }

    public struct AudioDetails
    {
        public double Size { get; set; }
        public int Bitrate { get; set; }
        public int AudioRate { get; set; }
    }

    public enum MediaType
    {
        Video,
        Audio,
        ImageJpg,
        ImagePng,
        ImageGif
    }

    public struct ValueProgress(double actionProgress, string actionProgressText)
    {
        public double ActionProgress { get; set; } = actionProgress;
        public string ActionProgressText { get; set; } = actionProgressText;
    }
}
