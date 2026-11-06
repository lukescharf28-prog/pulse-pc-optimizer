param(
 [Parameter(Mandatory=$true)][string]$Executable,
 [Parameter(Mandatory=$true)][string]$PrivateKey,
 [Parameter(Mandatory=$true)][string]$Version,
 [Parameter(Mandatory=$true)][string]$Output,
 [string]$Notes = 'Neue Pulse-Version'
)
$ErrorActionPreference = 'Stop'
$binary = Get-Item -LiteralPath $Executable
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($binary.FullName)
$normalizedVersion = ([Version]$Version).ToString()
if ($fileVersion.FileVersion -ne $normalizedVersion) { throw 'Dateiversion und Manifestversion müssen übereinstimmen (z. B. 1.1.0.0).' }
$tag = 'v' + ([Version]$Version).ToString(3)
$payload = [ordered]@{
 Product='Pulse'; Version=$normalizedVersion
 Url="https://github.com/lukescharf28-prog/pulse-pc-optimizer/releases/download/$tag/Pulse.exe"
 Sha256=(Get-FileHash -LiteralPath $binary.FullName -Algorithm SHA256).Hash
 Size=$binary.Length; Notes=$Notes
}
$bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress))
$rsa = [Security.Cryptography.RSA]::Create()
try {
 $rsa.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKey)))
 $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
 $envelope = @{ Payload=[Convert]::ToBase64String($bytes); Signature=[Convert]::ToBase64String($signature) } | ConvertTo-Json
 [IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),$envelope)
} finally { $rsa.Dispose() }
Write-Output "Signiertes Update-Manifest erstellt: $Output"
