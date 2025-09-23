using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;

namespace CompressMediaPage
{
    public class MainModel: INotifyPropertyChanged
    {
        private RadioItem _selectedoption;
        public RadioItem SelectedOption
        {
            get => _selectedoption;
            set => SetProperty(ref _selectedoption, value);
        }
        private OperationState _state;
        public OperationState State
        {
            get => _state;
            set => SetProperty(ref _state, value, alsoNotify: [nameof(BeforeOperation), nameof(DuringOperation), nameof(AfterOperation)]);
        }
        private bool _processpaused;
        public bool ProcessPaused
        {
            get => _processpaused;
            set => SetProperty(ref _processpaused, value);
        }

        public bool BeforeOperation => State == OperationState.BeforeOperation;
        public bool DuringOperation => State == OperationState.DuringOperation;
        public bool AfterOperation => State == OperationState.AfterOperation;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }

    public class SizeOrBitrateModel: INotifyPropertyChanged
    {
        public bool IsBitrate { get; set; }
        public string Unit { get; set; }
        private string _originalvalue;
        public string OriginalValue
        {
            get => _originalvalue;
            set => SetProperty(ref _originalvalue, value);
        }
        private double _specifiedvalue;
        public double SpecifiedValue
        {
            get => _specifiedvalue;
            set => SetProperty(ref _specifiedvalue, value);
        }
        private bool _limittotarget;
        public bool LimitToTarget
        {
            get => _limittotarget;
            set => SetProperty(ref _limittotarget, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }

    public class ResolutionModel: INotifyPropertyChanged
    {
        public ObservableCollection<Size> Options { get; set; }
        private string _originalresolution;
        public string OriginalResolution
        {
            get => _originalresolution;
            set => SetProperty(ref _originalresolution, value);
        }
        private int _customwidth;
        public int CustomWidth
        {
            get => _customwidth;
            set => SetProperty(ref _customwidth, value);
        }
        private int _customheight;
        public int CustomHeight
        {
            get => _customheight;
            set => SetProperty(ref _customheight, value);
        }
        private Size _selectedresolution;
        public Size SelectedResolution
        {
            get => _selectedresolution;
            set => SetProperty(ref _selectedresolution, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }

    public class SliderModel: INotifyPropertyChanged
    {
        private int _min;
        public int Min
        {
            get => _min;
            set => SetProperty(ref _min, value);
        }
        private int _max;
        public int Max
        {
            get => _max;
            set => SetProperty(ref _max, value);
        }
        private int _value;
        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value, alsoNotify: nameof(ValueString));
        }
        public bool SmallWidth { get; set; }

        public Func<int, string> ValueStringFunc { get; set; } = v => v.ToString();
        public string ValueString => ValueStringFunc(_value);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }

    public class DropdownModel: INotifyPropertyChanged
    {
        public ObservableCollection<Item> Options { get; set; }
        public string Label { get; set; }
        private string _originalValue;
        public string OriginalValue
        {
            get => _originalValue;
            set => SetProperty(ref _originalValue, value);
        }
        private Item _selectedValue;
        public Item SelectedValue
        {
            get => _selectedValue;
            set => SetProperty(ref _selectedValue, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }

        public class Item
        {
            public double Value { get; set; }
            public string Unit { get; set; }
            public override string ToString() => $"{Value} {Unit}";
        }
    }

    public class RateFactorModel
    {
        public SliderModel CRFSlider { get; set; }
        public SliderModel PresetSlider { get; set; }
    }

    public class OptionsProps
    {
        public string FileName { get; set; }
        public MediaType MediaType { get; set; }
        public string IconGlyph { get; set; }
        public ObservableCollection<RadioItem> Options { get; set; }
        public int Columns { get; set; }
        public SizeOrBitrateModel SizeViewModel { get; set; }
        public SizeOrBitrateModel VideoBitrateViewModel { get; set; }
        public ResolutionModel ResolutionModel { get; set; }
        public RateFactorModel RateFactorModel { get; set; }
        public DropdownModel AudioBitrateModel { get; set; }
        public DropdownModel AudioSampleRateModel { get; set; }
        public DropdownModel FpsModel { get; set; }
        public SliderModel AudioQuality { get; set; }
        public SliderModel ImageQuality { get; set; }
    }

    public class RadioItem
    {
        public string Title { get; set; }
        public CompressionMethod Method { get; set; }
        public override string ToString() => Title;
    }

    public enum CompressionMethod
    {
        FileSize,
        VideoBitrate,
        AudioBitrate,
        Resolution,
        FPS,
        CRF, // Constant Rate Factor for videos
        QV, // Quality Value for images
        QA, // Audio Quality for audio files
        AR, // Audio Rate for audio files
    }

    public enum OperationState
    {
        BeforeOperation, DuringOperation, AfterOperation
    }
}
