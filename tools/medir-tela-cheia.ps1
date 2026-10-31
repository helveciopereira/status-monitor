# Mede quanto tempo o widget leva para SUMIR quando um app entra em tela cheia e para
# VOLTAR quando ele sai. Simula a tela cheia com um formulário sem borda do tamanho do
# monitor (o mesmo que um vídeo em tela cheia ou F11 faz). Rode na sessão interativa
# real (precisa da Shell_TrayWnd) com o VelocimetroInternet já aberto.
#   pwsh -File tools\medir-tela-cheia.ps1
#   -JaNaFrente: a janela abre pequena, vai para a frente e SÓ DEPOIS cresce para tela cheia
#                (= vídeo do navegador ou F11: não há troca de foco; só o aviso do shell pega).
param([int]$Rodadas = 3, [switch]$JaNaFrente)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class VW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  // O Windows só deixa um processo em segundo plano tomar o primeiro plano logo após uma
  // entrada de teclado; um Alt "solto" libera a trava (truque padrão de automação).
  public static bool Frente(IntPtr h){ keybd_event(0x12,0,0,UIntPtr.Zero); keybd_event(0x12,0,2,UIntPtr.Zero); SetForegroundWindow(h); return GetForegroundWindow()==h; }
  // Acha a janela do widget pelo título "VelocimetroInternetWidget"
  public static IntPtr Widget(uint pid){ IntPtr achou=IntPtr.Zero; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid){ var sb=new StringBuilder(64); GetWindowText(h,sb,64); if(sb.ToString()=="VelocimetroInternetWidget"){ achou=h; return false; } } return true; }, IntPtr.Zero); return achou; }
}
'@

$proc = Get-Process VelocimetroInternet -ErrorAction Stop | Select-Object -First 1
$hw = [VW]::Widget([uint32]$proc.Id)
if ($hw -eq [IntPtr]::Zero) { throw "Janela do widget não encontrada." }
if (-not [VW]::IsWindowVisible($hw)) { throw "O widget já está oculto antes do teste — inconclusivo." }

$tela = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
for ($i = 1; $i -le $Rodadas; $i++) {
    $f = New-Object System.Windows.Forms.Form
    $f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'; $f.BackColor = 'Black'
    $f.ShowInTaskbar = $false
    if ($JaNaFrente) {
        $f.Bounds = New-Object System.Drawing.Rectangle 200, 200, 800, 500
        $f.Show()
        $naFrente = [VW]::Frente($f.Handle)
        Start-Sleep -Milliseconds 700; [System.Windows.Forms.Application]::DoEvents()
        if (-not [VW]::IsWindowVisible($hw)) { "Rodada ${i}: widget sumiu com janela PEQUENA (falso positivo)" }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $f.Bounds = $tela   # cresce para tela cheia sem trocar o foco
    } else {
        $f.Bounds = $tela
        $f.Show()
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $naFrente = [VW]::Frente($f.Handle)
    }
    if (-not $naFrente) { "Rodada ${i}: AVISO — a janela de teste NÃO ficou em primeiro plano; resultado inválido" }
    while ([VW]::IsWindowVisible($hw) -and $sw.ElapsedMilliseconds -lt 5000) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 5 }
    $sumiu = $sw.ElapsedMilliseconds
    Start-Sleep -Milliseconds 400
    $sw.Restart(); $f.Close(); $f.Dispose()
    while (-not [VW]::IsWindowVisible($hw) -and $sw.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 5 }
    "Rodada {0}: sumiu em {1} ms · voltou em {2} ms" -f $i, $sumiu, $sw.ElapsedMilliseconds
    Start-Sleep -Milliseconds 600
}
