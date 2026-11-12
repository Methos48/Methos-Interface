param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "Methos.ico")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$size = 256
if (-not ([System.Management.Automation.PSTypeName]'MethosNative.NativeMethods').Type) {
    Add-Type -Namespace MethosNative -Name NativeMethods -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool DestroyIcon(System.IntPtr hIcon);'
}
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(25, 38, 56))

$outer = New-Object System.Drawing.Drawing2D.GraphicsPath
$outer.AddPolygon([System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(38, 198),
    [System.Drawing.Point]::new(38, 58),
    [System.Drawing.Point]::new(78, 58),
    [System.Drawing.Point]::new(128, 126),
    [System.Drawing.Point]::new(178, 58),
    [System.Drawing.Point]::new(218, 58),
    [System.Drawing.Point]::new(218, 198),
    [System.Drawing.Point]::new(178, 198),
    [System.Drawing.Point]::new(178, 116),
    [System.Drawing.Point]::new(128, 181),
    [System.Drawing.Point]::new(78, 116),
    [System.Drawing.Point]::new(78, 198)
))
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
    [System.Drawing.Color]::FromArgb(255, 74, 74),
    [System.Drawing.Color]::FromArgb(180, 12, 24),
    [System.Drawing.Drawing2D.LinearGradientMode]::Vertical
)
$graphics.FillPath($brush, $outer)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 150, 150), 3)
$graphics.DrawPath($pen, $outer)

$handle = $bitmap.GetHicon()
try {
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $stream = [System.IO.File]::Create($OutputPath)
    try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose() }
}
finally {
    [void][MethosNative.NativeMethods]::DestroyIcon($handle)
    $pen.Dispose(); $brush.Dispose(); $outer.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
Write-Host "Icono Methos creado: $OutputPath"

