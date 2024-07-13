$ErrorActionPreference = 'Continue'

# Check if the script is running with administrative privileges
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{
    # Restart the script with elevated privileges
    $newProcess = Start-Process -FilePath "powershell" -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    # Exit the current script
    exit
}

cd "$PSScriptRoot"

if (Test-Path -Path ".\certificate.pfx")
{
    Write-Host "certificate.pfx already exists. Delete it first if you want to create a new one."

    # Ask the user if the existing certificate shall be deleted
    $response = Read-Host "Do you want to delete the existing certificate? (y/n)"
    if ($response -eq "y")
	{
        $response = Read-Host "Also remove it from the local machine personal and trusted root certificate store? (y/n)"
        if ($response -eq "y")
	    {
	        # Remove this certificate also from the local machine Personal store
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.SendKeys]::SendWait("12345678{ENTER}")
            $cert = Get-PfxCertificate -FilePath ".\certificate.pfx"

            $store = Get-Item -Path Cert:\LocalMachine\My
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $store.Remove($cert)
            $store.Close()
            $store = $null

            $rootStore = Get-Item -Path Cert:\LocalMachine\Root
            $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $rootStore.Remove($cert)
            $rootStore.Close()
            $rootStore = $null

            $cert = $null
        }    
        Remove-Item -Path ".\certificate.pfx"
	}
	else
	{
		Write-Host "Exiting..."
        exit
    }
}

Write-Host "Creating a new self-signed certificate..."
$cert = New-SelfSignedCertificate -Subject localhost -DnsName localhost -FriendlyName "Discovery Client" -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

# Configure this certificate as trusted
Write-Host "Configuring the certificate as trusted..."
$rootStore = Get-Item -Path Cert:\LocalMachine\Root
$rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$rootStore.Add($cert)
$rootStore.Close()

Write-Host "Exporting the certificate to a PFX file..."
Export-PfxCertificate -Cert $cert -FilePath certificate.pfx -Password (ConvertTo-SecureString -String "12345678" -Force -AsPlainText)

Write-Host "Finished." + ([Environment]::NewLine)

Read-Host "Press any key to exit"
