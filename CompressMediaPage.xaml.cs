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
using static System.Net.WebRequestMethods;

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
        //private string ffmpegPath;
        private string outputFile;
        private readonly double progressMax = 1_000_000;
        private readonly double[] resolutionOptions = { 144, 360, 480, 720, 1080, 1440, 2160 };
        private readonly double[] audioBitrateOptions = { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
        private readonly double[] audioSampleRateOptions = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 };
        private readonly double[] fpsOptions = { 5, 10, 15, 24, 30, 50, 60, 72, 90, 100, 120, 144, 200, 240 };
        private const string audioBitrateUnit = "kb/s";
        private const string audioSampleRateUnit = "kHz";
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
            catch (NotSupportedException ex)
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
            //viewModel.SelectedOption = optionProps.Options.First();
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
                    Unit = "MB",
                    OriginalValue = "Calculating..."
                };
                optionProps.VideoBitrateViewModel = new SizeOrBitrateModel
                {
                    IsBitrate = true,
                    Unit = "kb/s",
                    OriginalValue = "Calculating..."
                };
                optionProps.ResolutionModel = new ResolutionModel
                {
                    Options = [default],
                    OriginalResolution = "Calculating..."
                };
                optionProps.RateFactorModel = new RateFactorModel
                {
                    CRFSlider = new SliderModel { Value = 25, Min = 10, Max = 40 },
                    PresetSlider = new SliderModel
                    {
                        Value = 0, Min = -1, Max = 1, SmallWidth = true,
                        ValueStringFunc = v => v switch
                        {
                            -1 => "fast",
                            0 => "medium",
                            1 => "slow",
                            _ => throw new ArgumentOutOfRangeException()
                        }
                    }
                };
                optionProps.AudioBitrateModel = GetDropdownModel(audioBitrateOptions, "bitrate", audioBitrateUnit);
                optionProps.AudioSampleRateModel = GetDropdownModel(audioSampleRateOptions, "sample rate", audioSampleRateUnit);
                optionProps.FpsModel = GetDropdownModel(fpsOptions, "fps", "FPS");
                optionProps.AudioQuality = new SliderModel { Value = 2, Min = 0, Max = 9 };
                optionProps.ImageQuality = new SliderModel { Value = 5, Min = 2, Max = 20 };
            }

            async Task CompleteOptionModels()
            {
                var failed = false;
                switch (optionProps.MediaType)
                {
                    case MediaType.Video or MediaType.ImageGif:
                    {
                        var videoDetails = await compressProcessor.GetVideoDetails(ErrorActionFromFfmpeg);
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
                        var audioDetails = await compressProcessor.GetAudioDetails(ErrorActionFromFfmpeg);
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
                        var originalRes = await compressProcessor.GetImageResolution(ErrorActionFromFfmpeg);
                        if (failed) return;
                        SetResolutionModel(originalRes);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                async void ErrorActionFromFfmpeg(string message)
                {
                    failed = true;
                    ErrorDialog.Title = "Compress operation failed";
                    ErrorDialog.Content = message;
                    await ErrorDialog.ShowAsync();
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
            viewModel.State = OperationState.DuringOperation;
            var valueProgress = new Progress<ValueProgress>(progress =>
            {
                CompressProgressValue.Value = progress.ActionProgress;
                CompressProgressText.Text = progress.ActionProgressText;
            });
            var failed = false;
            string? errorMessage = null;

            try
            {
                bool isAudio;
                switch (viewModel.SelectedOption.Method)
                {
                    case CompressionMethod.Resolution:
                        var width = optionProps.ResolutionModel.SelectedResolution.Width;
                        if(width == 0) width = optionProps.ResolutionModel.CustomWidth;
                        var isImage = optionProps.MediaType != MediaType.Video;
                        await compressProcessor.CompressResolution(width, isImage, progressMax, valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.VideoBitrate or CompressionMethod.AudioBitrate:
                        isAudio = optionProps.MediaType == MediaType.Audio;
                        var bitrate = isAudio ? optionProps.AudioBitrateModel.SelectedValue.Value : optionProps.VideoBitrateViewModel.SpecifiedValue;
                        await compressProcessor.CompressBitrate(bitrate, optionProps.VideoBitrateViewModel.LimitToTarget, isAudio,
                            progressMax, valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.FileSize:
                        isAudio = optionProps.MediaType == MediaType.Audio;
                        var size = optionProps.SizeViewModel.SpecifiedValue;
                        await compressProcessor.CompressSize(size, optionProps.VideoBitrateViewModel.LimitToTarget, isAudio, progressMax, valueProgress,
                            SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.FPS:
                        var isGif = optionProps.MediaType == MediaType.ImageGif;
                        await compressProcessor.CompressFPS(optionProps.FpsModel.SelectedValue.Value, isGif, progressMax, valueProgress,
                            SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.CRF:
                        await compressProcessor.CompressCRF(optionProps.RateFactorModel.CRFSlider.Value, optionProps.RateFactorModel.PresetSlider.ValueString,
                            progressMax, valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.QA:
                        await compressProcessor.CompressAudioQualityFactor(optionProps.AudioQuality.Value, progressMax,
                            valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.AR:
                        await compressProcessor.CompressAudioSamplingRate(optionProps.AudioSampleRateModel.SelectedValue.Value, progressMax,
                            valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                    case CompressionMethod.QV:
                        await compressProcessor.CompressImageQualityFactor(optionProps.ImageQuality.Value, progressMax,
                            valueProgress, SetOutputFile, ErrorActionFromFfmpeg);
                        break;
                }

                if (viewModel.State == OperationState.BeforeOperation) return; //Canceled
                if (failed)
                {
                    viewModel.State = OperationState.BeforeOperation;
                    await ErrorAction(errorMessage!);
                    await compressProcessor.Cancel(outputFile);
                    outputFile = null;
                    return;
                }

                viewModel.State = OperationState.AfterOperation;
            }
            catch (Exception ex)
            {
                await ErrorAction(ex.Message);
                viewModel.State = OperationState.BeforeOperation;
            }

            void ErrorActionFromFfmpeg(string message)
            {
                failed = true;
                errorMessage = message;
            }

            void SetOutputFile(string file)
            {
                outputFile = file;
            }

            async Task ErrorAction(string message)
            {
                ErrorDialog.Title = "Compress operation failed";
                ErrorDialog.Content = message;
                await ErrorDialog.ShowAsync();
            }
        }

        private void PauseOrViewCompress_OnClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.State == OperationState.AfterOperation)
            {
                compressProcessor.ViewFiles(outputFile);
                return;
            }

            if (viewModel.ProcessPaused)
            {
                compressProcessor.Resume();
                viewModel.ProcessPaused = false;
            }
            else
            {
                compressProcessor.Pause();
                viewModel.ProcessPaused = true;
            }
        }

        private void CancelOrCloseCompress_OnClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.State == OperationState.AfterOperation)
            {
                viewModel.State = OperationState.BeforeOperation;
                return;
            }

            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private async void CancelProcess(object sender, RoutedEventArgs e)
        {
            await compressProcessor.Cancel(outputFile);
            viewModel.State = OperationState.BeforeOperation;
            viewModel.ProcessPaused = false;
            CancelFlyout.Hide();
        }

        private void GoBack()
        {
            _ = compressProcessor.Cancel(outputFile);
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

    public class EnumToVisibilityConverter : IValueConverter
    {
        public CompressionMethod Enum { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not CompressionMethod enumValue) return Visibility.Collapsed;
            return enumValue.Equals(Enum) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
