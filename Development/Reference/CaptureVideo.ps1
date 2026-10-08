param([double[]]$SampleTimes = @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18), [string]$Prefix = 'frame-')
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$player = New-Object System.Windows.Media.MediaPlayer
$player.ScrubbingEnabled = $true
$player.Volume = 0
$player.Open([Uri]'E:\AQNPASXyqNuNdKOL3-dU4t7v_Rc53BV-aXucfoyjY0mjWfxdTjMCu7z3SvOtZvcba95v16w7QLNvaHTTX5Es0DARn-DZWt__U2Wyk2MrYQ.mp4')
function Pump($milliseconds) {
    $until = [DateTime]::UtcNow.AddMilliseconds($milliseconds)
    while ([DateTime]::UtcNow -lt $until) {
        $frame = New-Object System.Windows.Threading.DispatcherFrame
        $null = [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, [Action]{ $frame.Continue = $false })
        [System.Windows.Threading.Dispatcher]::PushFrame($frame)
        Start-Sleep -Milliseconds 15
    }
}
$player.Play()
Pump 1500
$player.Pause()
Write-Output ('Video: ' + $player.NaturalVideoWidth + 'x' + $player.NaturalVideoHeight + ', ' + $player.NaturalDuration)
foreach ($seconds in $SampleTimes) {
    if ($player.NaturalDuration.HasTimeSpan -and $seconds -ge $player.NaturalDuration.TimeSpan.TotalSeconds) { continue }
    $player.Position = [TimeSpan]::FromSeconds($seconds)
    Pump 250
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawVideo($player, [System.Windows.Rect]::new(0, 0, 360, 640))
    $context.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(360, 640, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $output = [System.IO.File]::Create((Join-Path $PSScriptRoot ($Prefix + $seconds + '.png')))
    $encoder.Save($output)
    $output.Dispose()
}
$player.Close()
