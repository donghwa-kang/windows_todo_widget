# Windows에서 외부 이미지 도구 없이 여러 해상도의 ICO를 재생성합니다.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assetDir=Join-Path (Split-Path $PSScriptRoot) 'Assets'
$frames=@()
foreach($size in @(16,24,32,48,64,128,256)) {
    $bitmap=New-Object System.Drawing.Bitmap $size,$size
    $g=[Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size/256.0,$size/256.0)
    $shape=New-Object Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(8,8,104,104,180,90);$shape.AddArc(144,8,104,104,270,90)
    $shape.AddArc(144,144,104,104,0,90);$shape.AddArc(8,144,104,104,90,90);$shape.CloseFigure()
    $fill=New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#090d0c'))
    $border=New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#34473c')),6
    $g.FillPath($fill,$shape);$g.DrawPath($border,$shape)
    foreach($stroke in (,@('#83c99c',62,130,108,174,196,80))) {
        $pen=New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml($stroke[0])),18
        $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
        $points=[Drawing.PointF[]]@([Drawing.PointF]::new($stroke[1],$stroke[2]),[Drawing.PointF]::new($stroke[3],$stroke[4]),[Drawing.PointF]::new($stroke[5],$stroke[6]))
        $g.DrawLines($pen,$points);$pen.Dispose()
    }
    $stream=New-Object IO.MemoryStream
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames+=,@{Size=$size;Bytes=$stream.ToArray()}
    if($size -eq 256){$bitmap.Save((Join-Path $assetDir 'app.png'),[Drawing.Imaging.ImageFormat]::Png)}
    $stream.Dispose();$border.Dispose();$fill.Dispose();$shape.Dispose();$g.Dispose();$bitmap.Dispose()
}
$out=[IO.File]::Create((Join-Path $assetDir 'app.ico'));$writer=New-Object IO.BinaryWriter $out
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames){$dimension=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length}
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose();$out.Dispose()}
