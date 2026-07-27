# ============================================================================
#  Genera una clave de licencia de Veradel (firmada con RSA-SHA256).
#
#  Uso:
#    .\New-VeradelLicense.ps1 -Name "Taller Pérez SL"
#    .\New-VeradelLicense.ps1 -Name "Juan" -Email "juan@x.com" -DaysValid 365
#
#  -DaysValid 0 (por defecto) = licencia perpetua.
#
#  Requiere la clave privada generada una sola vez en:
#    %USERPROFILE%\VeradelLicensing\private.xml
#  Esa clave NUNCA debe salir de tu equipo ni subirse a git. Si se pierde,
#  no se pueden emitir más licencias (habría que cambiar la clave pública
#  embebida en LicenseService.cs y recompilar).
#
#  La clave resultante se copia al portapapeles y se muestra en pantalla.
#  Formato: base64(payload).base64(firma), payload = "VRDL1|nombre|email|expiraTicks".
# ============================================================================
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Email = "",
    [int]$DaysValid = 0,
    [string]$PrivateKeyPath = "$env:USERPROFILE\VeradelLicensing\private.xml"
)

if ($Name.Contains("|") -or $Email.Contains("|")) {
    throw "El nombre y el email no pueden contener el caracter '|'."
}
if (-not (Test-Path $PrivateKeyPath)) {
    throw "No existe la clave privada: $PrivateKeyPath"
}

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString((Get-Content $PrivateKeyPath -Raw))

$expiry = 0
if ($DaysValid -gt 0) {
    $expiry = (Get-Date).ToUniversalTime().AddDays($DaysValid).Ticks
}

$payload = "VRDL1|$Name|$Email|$expiry"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
$signature = $rsa.SignData($bytes, "SHA256")

$key = [Convert]::ToBase64String($bytes) + "." + [Convert]::ToBase64String($signature)

Write-Output ""
Write-Output "Licencia para : $Name $(if ($Email) { "<$Email>" })"
Write-Output "Caducidad     : $(if ($expiry -eq 0) { 'perpetua' } else { (New-Object DateTime($expiry)).ToString('dd/MM/yyyy') + ' (UTC)' })"
Write-Output ""
Write-Output $key
Write-Output ""

try {
    Set-Clipboard -Value $key
    Write-Output "(copiada al portapapeles)"
} catch { }
