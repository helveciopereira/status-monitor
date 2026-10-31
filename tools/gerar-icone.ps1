# Gera src\VelocimetroInternet\Recursos\app.ico (16, 20, 24, 32, 40, 48, 64, 256 px) no estilo
# Modelo 3 (Acrylic Pill): quadrado arredondado escuro com seta ↑ azul (#4CC2FF) à esquerda e
# seta ↓ coral (#FF6B6B) à direita. O .ico vira o ícone do .exe (ApplicationIcon) e da bandeja.
#   pwsh -File tools\gerar-icone.ps1
Add-Type -AssemblyName System.Drawing
$raiz = Split-Path $PSScriptRoot -Parent
$destino = Join-Path $raiz 'src\VelocimetroInternet\Recursos\app.ico'
New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null

function Desenhar([int]$t) {
    $bmp = New-Object Drawing.Bitmap $t, $t, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)

    # Fundo: quadrado arredondado #202020 com borda sutil (raio ~22% do lado)
    $r = [Math]::Max(2, [int]($t * 0.22)); $d = $r * 2; $m = [Math]::Max(0, [int]($t * 0.02))
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $w = $t - 1 - 2 * $m
    $p.AddArc($m, $m, $d, $d, 180, 90); $p.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $p.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $p.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $p.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 32, 32, 32))), $p)
    if ($t -ge 24) { $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(70, 255, 255, 255)), ([Math]::Max(1, $t / 64))), $p) }

    # Setas: haste + ponta triangular, proporcionais ao tamanho
    $azul = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0x4C, 0xC2, 0xFF))
    $coral = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 0xFF, 0x6B, 0x6B))
    $haste = $t * 0.13; $ponta = $t * 0.30; $topo = $t * 0.18; $base = $t * 0.82
    foreach ($s in @(@{cx = $t * 0.33; up = $true; b = $azul }, @{cx = $t * 0.67; up = $false; b = $coral })) {
        $cx = $s.cx
        if ($s.up) {
            $tri = [Drawing.PointF[]]@((New-Object Drawing.PointF $cx, $topo), (New-Object Drawing.PointF ($cx + $ponta / 2), ($topo + $ponta * 0.75)), (New-Object Drawing.PointF ($cx - $ponta / 2), ($topo + $ponta * 0.75)))
            $g.FillPolygon($s.b, $tri)
            $g.FillRectangle($s.b, [float]($cx - $haste / 2), [float]($topo + $ponta * 0.7), [float]$haste, [float]($base - $topo - $ponta * 0.7))
        } else {
            $tri = [Drawing.PointF[]]@((New-Object Drawing.PointF $cx, $base), (New-Object Drawing.PointF ($cx + $ponta / 2), ($base - $ponta * 0.75)), (New-Object Drawing.PointF ($cx - $ponta / 2), ($base - $ponta * 0.75)))
            $g.FillPolygon($s.b, $tri)
            $g.FillRectangle($s.b, [float]($cx - $haste / 2), [float]$topo, [float]$haste, [float]($base - $topo - $ponta * 0.7))
        }
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return , $ms.ToArray()
}

# Monta o .ico com entradas PNG (formato aceito pelo Windows Vista+)
$tamanhos = 16, 20, 24, 32, 40, 48, 64, 256
$imagens = $tamanhos | ForEach-Object { , (Desenhar $_) }
$saida = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $saida
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dados = $imagens[$i]
    $bw.Write([byte]($t % 256)); $bw.Write([byte]($t % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$dados.Length); $bw.Write([uint32]$offset)
    $offset += $dados.Length
}
foreach ($dados in $imagens) { $bw.Write($dados) }
[IO.File]::WriteAllBytes($destino, $saida.ToArray())
"Ícone gerado: $destino ({0:N1} KB)" -f ((Get-Item $destino).Length / 1KB)

# Prévia ampliada para conferência visual
$prev = Join-Path $raiz 'tools\app-icone-previa.png'
[IO.File]::WriteAllBytes($prev, (Desenhar 256))
