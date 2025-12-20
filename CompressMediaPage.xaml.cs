using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinUIShared.Controls;
using WinUIShared.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CompressMediaPage
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class CompressMediaPage : Page
    {
        private string? navigateTo;
        private string? outputFile;
        private readonly double[] resolutionOptions = [144, 360, 480, 720, 1080, 1440, 2160];
        private readonly double[] audioBitrateOptions = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        private readonly double[] audioSampleRateOptions = [8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000];
        private readonly double[] fpsOptions = [1, 5, 10, 15, 24, 30, 50, 60, 72, 90, 100, 120, 144, 200, 240];
        private const string BitrateUnit = "kb/s";
        private const string SizeUnit = "MB";
        private MainModel viewModel = new();
        private CompressProcessor compressProcessor;
        private OptionsProps optionProps;

        public CompressMediaPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is not CompressProps props) return;
            navigateTo = props.TypeToNavigateTo;
            compressProcessor = new CompressProcessor(props.FfmpegPath, props.MediaPath);
            await ProcessMedia(props.MediaPath);
        }

        private async Task ProcessMedia(string path)
        {
            MediaType mediaType;
            try
            {
                mediaType = CompressProcessor.GetMediaType(path);
            }
            catch (NotSupportedException)
            {
                ErrorDialog.Title = "Unsupported media type";
                ErrorDialog.Content = "The file you selected is currently not supported by this application.";
                ErrorDialog.Loaded += async (s, e) =>
                {
                    Grid.Visibility = Visibility.Collapsed;
                    await ErrorDialog.ShowAsync();
                    GoBack();
                };
                return;
            }

            optionProps = new OptionsProps
            {
                MediaType = mediaType,
                FileName = CompressProcessor.GetFileName(path),
                IconGlyph = GetIconGlyph(mediaType),
                Options = new ObservableCollection<RadioItem>(mediaType switch
                {
                    MediaType.Video => GetRadioItemsForCompMethods([
                        CompressionMethod.FileSize, CompressionMethod.VideoBitrate, CompressionMethod.Resolution,
                        CompressionMethod.CRF, CompressionMethod.FPS
                    ]),
                    MediaType.Audio => GetRadioItemsForCompMethods([
                        CompressionMethod.FileSize, CompressionMethod.AudioBitrate, CompressionMethod.QA, CompressionMethod.AR
                    ]),
                    MediaType.ImageJpg => GetRadioItemsForCompMethods([
                        CompressionMethod.Resolution, CompressionMethod.QV
                    ]),
                    MediaType.ImagePng => GetRadioItemsForCompMethods([
                        CompressionMethod.Resolution
                    ]),
                    MediaType.ImageGif => GetRadioItemsForCompMethods([
                        CompressionMethod.Resolution, CompressionMethod.FPS
                    ]),
                    _ => throw new ArgumentOutOfRangeException()
                })
            };
            optionProps.Columns = optionProps.Options.Count >= 3 ? 3 : optionProps.Options.Count;
            viewModel.SelectedOption = optionProps.Options[0];
            HardwareSelector.Visibility = mediaType == MediaType.Video ? Visibility.Visible : Visibility.Collapsed;
            HardwareSelector.RegisterPropertyChangedCallback(HardwareSelector.SelectedGpuProperty, OnGpuChanged);
            InitializeOptionModels();
            await CompleteOptionModels();

            static string GetIconGlyph(MediaType mediaType) => mediaType switch
            {
                MediaType.Video => "\uE714",
                MediaType.Audio => "\uE8D6",
                MediaType.ImageJpg or MediaType.ImagePng => "\uE91B",
                MediaType.ImageGif => "\uF4A9",
                _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null)
            };

            static IEnumerable<RadioItem> GetRadioItemsForCompMethods(CompressionMethod[] methods)
            {
                foreach (var method in methods)
                {
                    yield return new RadioItem { Method = method, Title = GetTitleForCompressionMethod(method) };
                }
            }

            static string GetTitleForCompressionMethod(CompressionMethod method)
            {
                return method switch
                {
                    CompressionMethod.FileSize => "Target File Size",
                    CompressionMethod.VideoBitrate or CompressionMethod.AudioBitrate => "Target Bitrate",
                    CompressionMethod.Resolution => "Target Resolution",
                    CompressionMethod.FPS => "Target Frame Rate (FPS)",
                    CompressionMethod.CRF => "Constant Rate Factor (CRF)",
                    CompressionMethod.QV => "Quality Factor (QV)",
                    CompressionMethod.AR => "Audio Rate (AR)",
                    CompressionMethod.QA => "Quality Factor (QA)",
                    _ => throw new NotSupportedException($"Unsupported compression method: {method}"),
                };
            }

            void InitializeOptionModels()
            {
                optionProps.SizeViewModel = new SizeOrBitrateModel
                {
                    Unit = SizeUnit,
                    OriginalValue = "Calculating..."
                };
                optionProps.VideoBitrateViewModel = new SizeOrBitrateModel
                {
                    IsBitrate = true,
                    Unit = BitrateUnit,
                    OriginalValue = "Calculating..."
                };
                optionProps.ResolutionModel = new ResolutionModel
                {
                    Options = [default],
                    OriginalResolution = "Calculating..."
                };
                optionProps.RateFactorModel = new RateFactorModel
                {
                    CRFSlider = new SliderModel { Value = 25, Min = 10, Max = 40 }
                };
                optionProps.AudioBitrateModel = GetDropdownModel(audioBitrateOptions, "bitrate", BitrateUnit);
                optionProps.AudioSampleRateModel = GetDropdownModel(audioSampleRateOptions, "sample rate", "kHz");
                optionProps.FpsModel = GetDropdownModel(fpsOptions, "fps", "FPS");
                optionProps.AudioQuality = new SliderModel { Value = 2, Min = 0, Max = 9 };
                optionProps.ImageQuality = new SliderModel { Value = 5, Min = 2, Max = 20 };
                OnGpuChanged(HardwareSelector, HardwareSelector.SelectedGpuProperty);
            }

            async Task CompleteOptionModels()
            {
                var failed = false;
                switch (optionProps.MediaType)
                {
                    case MediaType.Video or MediaType.ImageGif:
                    {
                        var videoDetails = await compressProcessor.GetVideoDetails();
                        if (failed) return;

                        if (optionProps.MediaType == MediaType.Video)
                        {
                            if (optionProps.SizeViewModel.SpecifiedValue == 0) //If the user hasn't changed it
                            {
                                optionProps.SizeViewModel.SpecifiedValue = Math.Round(videoDetails.Size, 2);
                            }
                            optionProps.SizeViewModel.OriginalValue = $"{Math.Round(videoDetails.Size, 2)} {optionProps.SizeViewModel.Unit}";

                            if (optionProps.VideoBitrateViewModel.SpecifiedValue == 0) //If the user hasn't changed it
                            {
                                optionProps.VideoBitrateViewModel.SpecifiedValue = videoDetails.Bitrate;
                            }
                            optionProps.VideoBitrateViewModel.OriginalValue = $"{videoDetails.Bitrate} {optionProps.VideoBitrateViewModel.Unit}";
                        }

                        SetResolutionModel(videoDetails.Resolution);
                        SetUpDropdownModel(optionProps.FpsModel, fpsOptions, videoDetails.Fps);
                        break;
                    }

                    case MediaType.Audio:
                        var audioDetails = await compressProcessor.GetAudioDetails();
                        if (failed) return;

                        if (optionProps.SizeViewModel.SpecifiedValue == 0) //If the user hasn't changed it
                        {
                            optionProps.SizeViewModel.SpecifiedValue = Math.Round(audioDetails.Size, 2);
                        }
                        optionProps.SizeViewModel.OriginalValue = $"{Math.Round(audioDetails.Size, 2)} {optionProps.SizeViewModel.Unit}";

                        SetUpDropdownModel(optionProps.AudioBitrateModel, audioBitrateOptions, audioDetails.Bitrate);
                        SetUpDropdownModel(optionProps.AudioSampleRateModel, audioSampleRateOptions, audioDetails.AudioRate);
                        break;

                    case MediaType.ImageJpg:
                    case MediaType.ImagePng:
                        var originalRes = await compressProcessor.GetImageResolution();
                        if (failed) return;
                        SetResolutionModel(originalRes);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                static void SetUpDropdownModel(DropdownModel model, double[] options, double originalValue)
                {
                    model.SelectedValue = model.Options.Last(); //This is needed to avoid a weird NullReferenceException
                    foreach (var bitrateOption in options)
                    {
                        if (bitrateOption >= originalValue) model.Options.RemoveAt(0);
                    }
                    var originalRate = new DropdownModel.Item { Value = originalValue, Unit = model.Options.First().Unit };
                    model.Options.Insert(0, originalRate);
                    model.SelectedValue = originalRate;
                    model.OriginalValue = $"{originalRate.Value} {originalRate.Unit}";
                }
            }

            void SetResolutionModel(Size originalRes)
            {
                var resolutionHeightOptions = resolutionOptions.Where(h => h < originalRes.Height).ToList();
                var aspectRatio = originalRes.Width / (double)originalRes.Height;
                var options = new ObservableCollection<Size>(resolutionHeightOptions.Select(GetResolutionFromOption));
                foreach (var resolutionOption in options)
                {
                    optionProps.ResolutionModel.Options.Insert(0, resolutionOption);
                }
                optionProps.ResolutionModel.Options.Insert(0, originalRes); //Add original resolution as first option
                optionProps.ResolutionModel.SelectedResolution = originalRes;
                optionProps.ResolutionModel.CustomWidth = originalRes.Width;
                optionProps.ResolutionModel.CustomHeight = originalRes.Height;
                optionProps.ResolutionModel.OriginalResolution = $"{originalRes.Width} x {originalRes.Height}";
                optionProps.ResolutionModel.PropertyChanged += (sender, args) =>
                {
                    switch (args.PropertyName)
                    {
                        case nameof(optionProps.ResolutionModel.CustomWidth):
                            optionProps.ResolutionModel.CustomHeight = Convert.ToInt32(optionProps.ResolutionModel.CustomWidth / aspectRatio);
                            break;
                        case nameof(optionProps.ResolutionModel.CustomHeight):
                            optionProps.ResolutionModel.CustomWidth = Convert.ToInt32(optionProps.ResolutionModel.CustomHeight * aspectRatio);
                            break;
                    }
                };

                Size GetResolutionFromOption(double dimension)
                {
                    var isPortrait = aspectRatio < 1;
                    var otherDimension = Convert.ToInt32(isPortrait ? dimension / aspectRatio : dimension * aspectRatio);
                    if (otherDimension % 2 != 0) otherDimension += 1;
                    return isPortrait ? new Size((int)dimension, otherDimension) : new Size(otherDimension, (int)dimension);
                }
            }

            DropdownModel GetDropdownModel(double[] options, string label, string unit)
            {
                var model = new DropdownModel
                {
                    Options = new ObservableCollection<DropdownModel.Item>(options.Reverse()
                            .Select(i => new DropdownModel.Item{ Value = i, Unit = unit })),
                    Label = label,
                    OriginalValue = "Calculating..."
                };
                model.SelectedValue = model.Options.First();
                return model;
            }
        }

        private async void Compress(object sender, RoutedEventArgs e)
        {
            viewModel.NewSize = null;
            bool isAudio;
            Task processTask;
            switch (viewModel.SelectedOption.Method)
            {
                case CompressionMethod.Resolution:
                    var width = optionProps.ResolutionModel.SelectedResolution.Width;
                    if (width == 0) width = optionProps.ResolutionModel.CustomWidth;
                    var isImage = optionProps.MediaType != MediaType.Video;
                    processTask = compressProcessor.CompressResolution(width, isImage);
                    break;
                case CompressionMethod.VideoBitrate or CompressionMethod.AudioBitrate:
                    isAudio = optionProps.MediaType == MediaType.Audio;
                    var bitrate = isAudio ? optionProps.AudioBitrateModel.SelectedValue.Value : optionProps.VideoBitrateViewModel.SpecifiedValue;
                    processTask = compressProcessor.CompressBitrate(bitrate, optionProps.VideoBitrateViewModel.LimitToTarget, isAudio);
                    break;
                case CompressionMethod.FileSize:
                    isAudio = optionProps.MediaType == MediaType.Audio;
                    var size = optionProps.SizeViewModel.SpecifiedValue;
                    processTask = compressProcessor.CompressSize(size, optionProps.VideoBitrateViewModel.LimitToTarget, isAudio);
                    break;
                case CompressionMethod.FPS:
                    var isGif = optionProps.MediaType == MediaType.ImageGif;
                    processTask = compressProcessor.CompressFps(optionProps.FpsModel.SelectedValue.Value, isGif);
                    break;
                case CompressionMethod.CRF:
                    processTask = compressProcessor.CompressCrf(optionProps.RateFactorModel.CRFSlider.Value, optionProps.RateFactorModel.PresetSlider.ValueString);
                    break;
                case CompressionMethod.QA:
                    processTask = compressProcessor.CompressAudioQualityFactor(optionProps.AudioQuality.Value);
                    break;
                case CompressionMethod.AR:
                    processTask = compressProcessor.CompressAudioSamplingRate(optionProps.AudioSampleRateModel.SelectedValue.Value);
                    break;
                case CompressionMethod.QV:
                    processTask = compressProcessor.CompressImageQualityFactor(optionProps.ImageQuality.Value);
                    break;
                default: throw new NotImplementedException();
            }

            compressProcessor.SetInitialProgressTexts();
            outputFile = await ProcessManager.StartProcess(processTask);
            if (outputFile != null) viewModel.NewSize = $"{Math.Round(compressProcessor.GetFileSize(outputFile!), 2)} {SizeUnit}";
        }

        private void OnGpuChanged(DependencyObject sender, DependencyProperty dp)
        {
            VideoQualityLabel.Text = HardwareSelector.SelectedGpu?.Vendor switch
            {
                GpuVendor.Nvidia or GpuVendor.Intel => "CQ",
                GpuVendor.Amd => "QP",
                _ => "CRF"
            };
            foreach (var compressOption in optionProps.Options)
            {
                if (compressOption.Method == CompressionMethod.CRF)
                {
                    compressOption.Title = HardwareSelector.SelectedGpu?.Vendor switch
                    {
                        GpuVendor.Nvidia or GpuVendor.Intel => "Constant Quality (CQ)",
                        GpuVendor.Amd => "Constant QP (QP)",
                        _ => "Constant Rate Factor (CRF)"
                    };
                }
            }
            optionProps.RateFactorModel.PresetSlider = HardwareSelector.SelectedGpu?.Vendor switch
            {
                GpuVendor.Nvidia => new SliderModel
                {
                    Value = 0, Min = -2, Max = 9,
                    ValueStringFunc = v => v switch
                    {
                        -2 => "default",
                        -1 => "slow",
                        0 => "medium",
                        1 => "fast",
                        2 => "hp",
                        3 => "hq",
                        4 => "bd",
                        5 => "ll",
                        6 => "llhq",
                        7 => "llhp",
                        8 => "lossless",
                        9 => "losslesshp",
                        _ => throw new ArgumentOutOfRangeException()
                    }
                },
                GpuVendor.Amd => new SliderModel
                {
                    Value = 0, Min = -1, Max = 1, SmallWidth = true,
                    ValueStringFunc = v => v switch
                    {
                        -1 => "speed",
                        0 => "balanced",
                        1 => "quality",
                        _ => throw new ArgumentOutOfRangeException()
                    }
                },
                GpuVendor.Intel => new SliderModel
                {
                    Value = 0, Min = -2, Max = 2,
                    ValueStringFunc = v => v switch
                    {
                        -2 => "veryfast",
                        -1 => "fast",
                        0 => "medium",
                        1 => "slow",
                        2 => "veryslow",
                        _ => throw new ArgumentOutOfRangeException()
                    }
                },
                _ => new SliderModel
                {
                    Value = 0, Min = -5, Max = 4,
                    ValueStringFunc = v => v switch
                    {
                        -5 => "ultrafast",
                        -4 => "superfast",
                        -3 => "veryfast",
                        -2 => "faster",
                        -1 => "fast",
                        0 => "medium",
                        1 => "slow",
                        2 => "slower",
                        3 => "veryslow",
                        4 => "placebo",
                        _ => throw new ArgumentOutOfRangeException()
                    }
                }
            };
        }

        private void GoBack()
        {
            _ = compressProcessor.Cancel();
            if (navigateTo == null) Frame.GoBack();
            else Frame.NavigateToType(Type.GetType(navigateTo), outputFile, new FrameNavigationOptions { IsNavigationStackEnabled = false });
        }

        private void GoBack(object sender, RoutedEventArgs e) => GoBack();
    }

    public class CompressProps
    {
        public string FfmpegPath { get; set; }
        public string MediaPath { get; set; }
        public string? TypeToNavigateTo { get; set; }
    }

    public class EnumToVisibilityConverter: EnumToVisibilityConverter<CompressionMethod>;
}
