# Signs the files given as arguments with the code-signing certificate whose thumbprint is in OPAGACHI_CERT
# (Current User > Personal certificate store: an imported .pfx, or a USB token's or cloud HSM's certificate).
# package.cmd calls this when OPAGACHI_CERT is set. Windows Smart App Control blocks apps without a signature
# that chains to a trusted authority, so a release must be signed this way. Built into Windows; nothing to install.
$ErrorActionPreference = 'Stop'
$cert = Get-Item "Cert:\CurrentUser\My\$env:OPAGACHI_CERT"
if (-not $cert.HasPrivateKey) { throw "Certificate $env:OPAGACHI_CERT has no private key on this PC." }
foreach ($file in $args) {
    $s = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    if (-not $s.SignerCertificate) { throw "$file was not signed: $($s.StatusMessage)" }
    # Valid only when the certificate chains to a trusted authority; anything else is still blocked.
    if ($s.Status -ne 'Valid') { Write-Warning "$file is signed but not trusted ($($s.Status)), so Smart App Control will still block it." }
    else { "signed $file" }
}
