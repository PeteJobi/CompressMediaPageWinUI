# Compress Media Page (WinUI 3)
This provides a reuseable WinUI 3 page with an interface for compressing media files of various types.

<img height="700" alt="image" src="https://github.com/user-attachments/assets/e936ec21-c00e-4d8c-8a28-e8dbf034600a" />

## How to use
Include this library into your WinUI solution and reference it in your WinUI project. Then navigate to the **CompressMediaPage** when the user requests for it, passing a **CompressProps** object as parameter.
The **CompressProps** object should contain the path to ffmpeg, the path to the input media file, and optionally, the full name of the Page type to navigate back to when the user is done. If this last parameter is provided, you can get the path to the file that was generated on the Compress Media page. If not, the user will be navigated back to whichever page called the Compress Media page and there'll be no parameters. 
```
private void GoToCompress(){
  var ffmpegPath = Path.Join(Package.Current.InstalledLocation.Path, "Assets/ffmpeg.exe");
  var mediaPath = Path.Join(Package.Current.InstalledLocation.Path, "Assets/video.mp4");
  Frame.Navigate(typeof(CompressMediaPage), new CompressProps { FfmpegPath = ffmpegPath, MediaPath = mediaPath, TypeToNavigateTo = typeof(MainPage).FullName });
}

protected override void OnNavigatedTo(NavigationEventArgs e)
{
    //outputFile is sent only if TypeToNavigateTo was specified in MixerProps.
    if (e.Parameter is string outputFile)
    {
        Console.WriteLine($"Path of output file is {outputFile}");
    }
}
```

You may check out [CompressMedia](https://github.com/PeteJobi/CompressMedia) to see a full application that uses this page.
