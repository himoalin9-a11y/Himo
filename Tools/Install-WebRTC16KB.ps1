[CmdletBinding()]
param([string]$Version = "144.7559.15")
$ErrorActionPreference='Stop'
$androidSdkRoot='C:\Program Files (x86)\Android\android-sdk'
$ndkRoot=Join-Path $androidSdkRoot 'ndk-bundle'
$readelf=Join-Path $ndkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
$packageAar=Join-Path $env:USERPROFILE '.nuget\packages\fswebrtc.bindings.maui.android\0.9.3.15\lib\net10.0-android36.0\libwebrtc.aar'
if(!(Test-Path $packageAar)){throw "FsWebRTC package AAR was not found: $packageAar"}
if(!(Test-Path $readelf)){throw "llvm-readelf.exe was not found: $readelf"}
$cacheRoot=Join-Path $env:LOCALAPPDATA 'Himo\WebRTC16KB'; New-Item -ItemType Directory -Force -Path $cacheRoot|Out-Null
$cachedAar=Join-Path $cacheRoot "android-$Version.aar"
$url="https://repo1.maven.org/maven2/io/github/webrtc-sdk/android/$Version/android-$Version.aar"
if(!(Test-Path $cachedAar)){Invoke-WebRequest -Uri $url -OutFile $cachedAar -UseBasicParsing}
if((Get-Item $cachedAar).Length -lt 1000000){throw "Invalid AAR: $cachedAar"}
$temp=Join-Path ([IO.Path]::GetTempPath()) ("HimoWebRTC16KB_"+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $temp|Out-Null
try{
 $zip=Join-Path $temp 'webrtc.zip'; $extract=Join-Path $temp 'extracted'; New-Item -ItemType Directory -Force -Path $extract|Out-Null
 Copy-Item $cachedAar $zip -Force; Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
 foreach($abi in 'arm64-v8a','x86_64'){
  $so=Join-Path $extract "jni\$abi\libjingle_peerconnection_so.so"
  if(!(Test-Path $so)){throw "Missing $abi/libjingle_peerconnection_so.so"}
  $loads=@(& $readelf -lW $so 2>$null|Select-String 'LOAD'); if(!$loads){throw "No ELF LOAD segments: $abi"}
  $bad=@(); foreach($line in $loads){$parts=($line.ToString()-split '\s+')|Where-Object {$_ -ne ''}; $a=$parts[-1]; if($a -match '^0x([0-9a-fA-F]+)$' -and [Convert]::ToInt64($Matches[1],16)-lt 0x4000){$bad+=$a}}
  if($bad.Count){throw "Not 16 KB aligned for $abi: $($bad -join ', ')"}
  Write-Host "OK: $abi is 16 KB ELF aligned."
 }
 $backup="$packageAar.before-16kb.bak"; if(!(Test-Path $backup)){Copy-Item $packageAar $backup}
 Copy-Item $cachedAar $packageAar -Force
 Write-Host "Replaced $packageAar with WebRTC $Version."
}finally{Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue}
