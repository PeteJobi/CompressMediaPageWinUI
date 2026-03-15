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
using WinUIShared.Controls;
using WinUIShared.Helpers;

namespace CompressMediaPage
{
    public class CompressProcessor(string ffmpegPath, string mediaPath) : Processor(ffmpegPath)
    {
        public async Task<VideoDetails> GetVideoDetails()
        {
            var size = GetFileSize(mediaPath);
            double bitrate = 0, fps = 0;
            int width = 0, height = 0;
            var valuesSet = false;

            await StartFfmpegProcess($"-i \"{mediaPath}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
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

        public async Task<AudioDetails> GetAudioDetails()
        {
            var size = GetFileSize(mediaPath);
            int bitrate = 0, sampleRate = 0;
            var valuesSet = false;

            await StartFfmpegProcess($"-i \"{mediaPath}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
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

        public async Task<Size> GetImageResolution()
        {
            int width = 0, height = 0;
            var valuesSet = false;
            await StartFfmpegProcess($"-i \"{mediaPath}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (valuesSet || string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream #\d+:\d+.*?: Video: .+?, (\d+)x(\d+)");
                if (matchCollection.Count == 0) return;
                width = int.Parse(matchCollection[0].Groups[1].Value);
                height = int.Parse(matchCollection[0].Groups[2].Value);
                valuesSet = true;
            });

            return new Size(width, height);
        }

        public double GetFileSize(string outputFile) => File.Exists(outputFile) ? new FileInfo(outputFile).Length / (1024.0 * 1024.0) : 0; // Size in MB

        public void SetInitialProgressTexts()
        {
            leftTextPrimary.Report("Compressing...");
            rightTextPrimary.Report("0.0 %");
        }

        private void ProgressHandler(double progress, TimeSpan currentTime, TimeSpan duration, int fps)
        {
            IncrementProgress(progress);
        }

        public async Task CompressResolution(int width, bool isImage)
        {
            var cpuScaleParam = $"scale={width}:-1";
            string scaleParams;
            switch (gpuInfo?.Vendor)
            {
                case GpuVendor.Nvidia:
                    scaleParams = $"scale_cuda=w={width}:h={width}*ih/iw";
                    break;
                case GpuVendor.Amd:
                    var gpuPixelFormat = await GetGpuPixelFormat(mediaPath);
                    var (hwDownArgs, hwUpArgs) = GpuInfo.FilterParams(gpuInfo, gpuPixelFormat);
                    scaleParams = $"{hwDownArgs}scale={width}:-1{hwUpArgs}";
                    break;
                case GpuVendor.Intel:
                    scaleParams = $"scale_qsv=w={width}:h={width}*ih/iw";
                    break;
                default:
                    scaleParams = cpuScaleParam;
                    break;
            }

            if (isImage)
            {
                await StartFfmpegProcess($"-i \"{mediaPath}\" -vf \"{cpuScaleParam}\" \"{GetOutputName(mediaPath)}\"", ProgressHandler); // Images do not support hardware acceleration. (they do, but it is not worth the complexity)
            }
            else
            {
                await StartFfmpegTranscodingProcessDefaultQuality([mediaPath], GetOutputName(mediaPath), $"-vf \"{scaleParams}\"", 
                    ProgressHandler, X265LineWatcher);
            }
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressFps(double fps, bool isGif)
        {
            var fpsParam = $"-r {fps}";
            if (isGif)
            {
                await StartFfmpegProcess($"-i \"{mediaPath}\" {fpsParam} \"{GetOutputName(mediaPath)}\"", ProgressHandler); // GIFs do not support hardware acceleration
            }
            else
            {
                await StartFfmpegTranscodingProcessDefaultQuality([mediaPath], GetOutputName(mediaPath), $"-fps_mode auto {fpsParam}", ProgressHandler, X265LineWatcher);
            }
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressSize(double sizeInMb, bool limitToTarget, bool isAudio)
        {
            var duration = TimeSpan.MinValue;
            var parsedAudioBitrate = 0;
            await StartFfmpegProcess($"-i \"{mediaPath}\"", (sender, args) =>
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
            await CompressBitrate(totalBitrate, limitToTarget, isAudio);
        }

        public async Task CompressBitrate(double bitrate, bool limitToTarget, bool isAudio)
        {
            var limitToTargetCommand = limitToTarget ? $"-maxrate:v {bitrate} -bufsize:v {bitrate}" : string.Empty;
            bitrate *= 1000;

            if (isAudio)
            {
                await StartProcessForAudioOrImage($"-b:a {bitrate} {limitToTargetCommand}");
            }
            else
            {
                await StartFfmpegTranscodingProcess([mediaPath], GetOutputName(mediaPath), "-threads 1",
                    $"-fps_mode passthrough -rc vbr -b:v {bitrate} {limitToTargetCommand} -c:v {GpuInfo.EncodingParams(gpuInfo)} -c:a copy",
                    ProgressHandler, X265LineWatcher);
            }
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressCrf(int crf, string preset)
        {
            await StartFfmpegTranscodingProcess([mediaPath], GetOutputName(mediaPath), crf, preset, string.Empty,
                ProgressHandler, X265LineWatcher);
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressAudioQualityFactor(int qa)
        {
            await StartProcessForAudioOrImage($"-c:a libmp3lame -q:a {qa}");
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressAudioSamplingRate(double ar)
        {
            var parsedAudioBitrate = 0;
            await StartFfmpegProcess($"-i \"{mediaPath}\"", (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                Debug.WriteLine(args.Data);
                if (parsedAudioBitrate != 0) return;
                var matchCollection = Regex.Matches(args.Data, @"\s*Stream .+: Audio: .+ (\d+) kb/s");
                if (matchCollection.Count == 0) return;
                parsedAudioBitrate = int.Parse(matchCollection[0].Groups[1].Value);
            });

            await StartProcessForAudioOrImage($"-c:a libmp3lame -ar {ar} -b:a {parsedAudioBitrate * 1000}");
            if (HasBeenKilled()) return;
            AllDone();
        }

        public async Task CompressImageQualityFactor(int qv)
        {
            await StartProcessForAudioOrImage($"-q:v {qv}");
            if (HasBeenKilled()) return;
            AllDone();
        }

        private async Task StartProcessForAudioOrImage(string extraArguments)
        {
            var duration = TimeSpan.MinValue;
            await StartFfmpegProcess($"-i \"{mediaPath}\" {extraArguments} \"{GetOutputName(mediaPath)}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                }
                if (!args.Data.StartsWith("size")) return;
                if (!CheckNoSpaceDuringProcess(args.Data))
                {
                    var matchCollection = Regex.Matches(args.Data, @"^size=\s*\d+KiB.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementProgress(TimeSpan.Parse(matchCollection[0].Groups[1].Value) / duration * 100);
                }
            });
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

        private string GetOutputName(string path)
        {
            var inputName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var parentFolder = Path.GetDirectoryName(path) ?? throw new FileNotFoundException($"The specified path does not exist: {path}");
            outputFile = Path.Combine(parentFolder, $"{inputName}_COMPRESSED{extension}");
            File.Delete(outputFile);
            return outputFile;
        }

        private void CheckX265Error(string line)
        {
            const string x265Error = "x265 [error]: ";
            if (!line.StartsWith(x265Error)) return;
            error(line[x265Error.Length..]);
        }

        private void X265LineWatcher(string line)
        {
            CheckX265Error(line);
        }

        private void IncrementProgress(double progress)
        {
            progressPrimary.Report(progress);
            rightTextPrimary.Report($"{Math.Round(progress, 2)} %");
        }

        private void AllDone()
        {
            progressPrimary.Report(ProgressMax);
            rightTextPrimary.Report("100 %");
        }
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
}
