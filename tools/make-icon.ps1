# Génère Assets/AutoClic.ico : curseur blanc + pastille d'enregistrement sur pastille bleue.
Add-Type -AssemblyName System.Drawing

# Chemin relatif au dépôt : le script doit rester exécutable par quiconque le clone.
$racine = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $racine "src\AutoClic.App\Assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$icoPath = Join-Path $outDir "AutoClic.ico"

$master = 512

function New-Master {
    param([int]$S)

    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- Fond : carré très arrondi, dégradé indigo -> bleu ---
    $margin = [int]($S * 0.045)
    $side = $S - (2 * $margin)
    $radius = [int]($S * 0.235)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($margin, $margin, $d, $d, 180, 90)
    $path.AddArc($margin + $side - $d, $margin, $d, $d, 270, 90)
    $path.AddArc($margin + $side - $d, $margin + $side - $d, $d, $d, 0, 90)
    $path.AddArc($margin, $margin + $side - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.Rectangle($margin, $margin, $side, $side)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 46, 60, 138),
        [System.Drawing.Color]::FromArgb(255, 38, 132, 214),
        45.0)
    $g.FillPath($brush, $path)

    # Liseré clair : détache l'icône d'un fond sombre.
    $pen = New-Object System.Drawing.Pen(
        [System.Drawing.Color]::FromArgb(60, 255, 255, 255), [single]($S * 0.012))
    $g.DrawPath($pen, $path)

    # --- Curseur : polygone classique, en blanc ---
    # Repère 0..100 rapporté à la surface utile.
    $ox = $S * 0.235
    $oy = $S * 0.175
    $k  = $S * 0.0056   # 100 unités ~ 56 % du côté

    $pts = @(
        @(0,0), @(0,72), @(19,56), @(31,86), @(45,80), @(33,50), @(56,47)
    )
    $poly = @()
    foreach ($p in $pts) {
        $poly += New-Object System.Drawing.PointF([single]($ox + $p[0] * $k), [single]($oy + $p[1] * $k))
    }

    # Ombre portée douce, pour que le blanc tienne sur le bleu clair.
    $shadow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shadow.AddPolygon([System.Drawing.PointF[]]$poly)
    $shift = New-Object System.Drawing.Drawing2D.Matrix
    $shift.Translate([single]($S * 0.014), [single]($S * 0.018))
    $shadow.Transform($shift)
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 0, 0, 0))), $shadow)

    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$poly)

    # --- Pastille d'enregistrement, en haut à droite ---
    $dotSize = $S * 0.235
    $dotX = $S * 0.615
    $dotY = $S * 0.135

    $halo = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
    $g.FillEllipse($halo, [single]($dotX - $S * 0.018), [single]($dotY + $S * 0.006), [single]($dotSize + $S * 0.036), [single]($dotSize + $S * 0.036))

    $dotRect = New-Object System.Drawing.RectangleF([single]$dotX, [single]$dotY, [single]$dotSize, [single]$dotSize)
    $dotBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $dotRect,
        [System.Drawing.Color]::FromArgb(255, 255, 106, 96),
        [System.Drawing.Color]::FromArgb(255, 214, 40, 48),
        90.0)
    $g.FillEllipse($dotBrush, $dotRect)

    $g.Dispose()
    return $bmp
}

$masterBmp = New-Master -S $master

# --- Assemblage de l'ICO (entrées PNG, Windows Vista et suivants) ---
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()

foreach ($size in $sizes) {
    $scaled = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gs = [System.Drawing.Graphics]::FromImage($scaled)
    $gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gs.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gs.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gs.Clear([System.Drawing.Color]::Transparent)
    $gs.DrawImage($masterBmp, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $gs.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += ,@($size, $ms.ToArray())
    $ms.Dispose()
    $scaled.Dispose()
}

$masterBmp.Dispose()

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

$w.Write([UInt16]0)                    # réservé
$w.Write([UInt16]1)                    # type : icône
$w.Write([UInt16]$payloads.Count)

$offset = 6 + (16 * $payloads.Count)
foreach ($entry in $payloads) {
    $size = $entry[0]
    $bytes = $entry[1]
    $w.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $w.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $w.Write([Byte]0)                  # palette
    $w.Write([Byte]0)                  # réservé
    $w.Write([UInt16]1)                # plans
    $w.Write([UInt16]32)               # bits par pixel
    $w.Write([UInt32]$bytes.Length)
    $w.Write([UInt32]$offset)
    $offset += $bytes.Length
}

foreach ($entry in $payloads) { $w.Write($entry[1]) }

$w.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$w.Dispose()
$out.Dispose()

"$icoPath : $((Get-Item $icoPath).Length) octets, $($payloads.Count) résolutions"

# Aperçu 256 px déposé à côté de l'icône, pour contrôle à l'œil.
$preview = Join-Path $outDir "AutoClic-preview.png"
[System.IO.File]::WriteAllBytes($preview, ($payloads | Where-Object { $_[0] -eq 256 })[1])
"$preview : aperçu 256 px"
