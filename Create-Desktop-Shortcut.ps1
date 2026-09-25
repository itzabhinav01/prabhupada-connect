$WshShell = New-Object -comObject WScript.Shell
$DesktopPath = [System.Environment]::GetFolderPath('Desktop')
$Shortcut = $WshShell.CreateShortcut("$DesktopPath\Prabhupada Connect.lnk")
$Shortcut.TargetPath = "$PSScriptRoot\Launch.bat"
$Shortcut.WorkingDirectory = "$PSScriptRoot"
$iconPath = "$PSScriptRoot\App\VedaBaseModern2.UI\Assets\AppIcon.ico"
if (Test-Path $iconPath) {
    $Shortcut.IconLocation = "$iconPath, 0"
}
$Shortcut.Description = "Prabhupāda Connect - Complete VedaBase Research Workstation"
$Shortcut.Save()
Write-Host "Desktop shortcut created successfully at: $DesktopPath\Prabhupada Connect.lnk" -ForegroundColor Green
Start-Sleep -Seconds 2
