[CmdletBinding()]
param([switch]$Initialize, [string]$Manifest,
      [string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'TableTennisReleaseSigning\release-key.dpapi'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$publicPath = Join-Path $PSScriptRoot '..\updater\release-public-key.xml'
$entropy = [Text.Encoding]::UTF8.GetBytes('TableTennisReleaseSigning-v1')
$parameters = New-Object Security.Cryptography.CspParameters
$parameters.ProviderType = 24
if ($Initialize) {
    if (Test-Path -LiteralPath $publicPath) { throw 'Public trust key already exists. Restore its private key rather than replacing the trust identity.' }
    if (Test-Path -LiteralPath $KeyPath) { throw 'Private key already exists. No files were overwritten.' }
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(3072, $parameters)
    $rsa.PersistKeyInCsp = $false
    try {
        $private = [Text.Encoding]::UTF8.GetBytes($rsa.ToXmlString($true))
        $protected = [Security.Cryptography.ProtectedData]::Protect($private, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($KeyPath)) -Force | Out-Null
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($publicPath)) -Force | Out-Null
        [IO.File]::WriteAllBytes($KeyPath, $protected)
        [IO.File]::WriteAllText($publicPath, $rsa.ToXmlString($false), [Text.Encoding]::ASCII)
    } finally { $rsa.Dispose(); if ($private) { [Array]::Clear($private, 0, $private.Length) } }
    Write-Output 'Created public update trust key and a private key protected for this Windows user.'
    exit 0
}
if (-not $Manifest -or -not (Test-Path -LiteralPath $Manifest -PathType Leaf)) { throw 'Select an existing manifest to sign.' }
if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) { throw 'Release signing key is missing. It is never downloaded or regenerated automatically.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider($parameters)
$rsa.PersistKeyInCsp = $false
try {
    $private = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyPath), $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($private))
    if ($rsa.ToXmlString($false) -ne [IO.File]::ReadAllText($publicPath)) { throw 'Signing key does not match the pinned public key.' }
    $data = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Manifest))
    $signature = $rsa.SignData($data, 'SHA256')
    [IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Manifest) + '.sig', $signature)
} finally { $rsa.Dispose(); if ($private) { [Array]::Clear($private, 0, $private.Length) } }
Write-Output 'Signed update manifest. Private key material was not exported.'
