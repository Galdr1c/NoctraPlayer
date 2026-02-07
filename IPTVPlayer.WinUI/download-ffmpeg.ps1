# PowerShell script
# IPTVPlayer.WinUI/download-ffmpeg.ps1

$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
$output = "ffmpeg.zip"
$extractPath = "FFmpeg"
$tempDir = "temp_ffmpeg"

Write-Host "Downloading FFmpeg from $url..."
Invoke-WebRequest -Uri $url -OutFile $output

Write-Host "Extracting..."
# Expand to a temporary directory first
Expand-Archive -Path $output -DestinationPath $tempDir -Force

# Create the destination directory if it doesn't exist
if (!(Test-Path -Path $extractPath)) {
    New-Item -ItemType Directory -Force -Path $extractPath
}

# Find the bin folder inside the extracted structure (it usually has a versioned folder name)
$binPath = Get-ChildItem -Path $tempDir -Recurse -Filter "bin" -Directory | Select-Object -First 1

if ($binPath) {
    Write-Host "Found bin folder at $($binPath.FullName)"
    Copy-Item -Path "$($binPath.FullName)\*.dll" -Destination $extractPath
    Write-Host "DLLs copied to $extractPath"
} else {
    Write-Error "Could not find 'bin' folder in extracted archive."
}

# Cleanup
Remove-Item -Recurse -Force $tempDir
Remove-Item $output

Write-Host "FFmpeg binaries ready in $extractPath/"
