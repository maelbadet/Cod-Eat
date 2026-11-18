param([switch]$Initialize)
$ErrorActionPreference = 'Stop'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Demo__Sqlite = 'true'
$env:BookStack__Simulated = 'true'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src/CodEat.Web'
if ($Initialize) {
    $env:Bootstrap__Email = Read-Host 'Adresse e-mail du premier compte administrateur'
    $firstSecret = Read-Host 'Mot de passe (12 caractères, majuscule, minuscule, chiffre et symbole)' -AsSecureString
    $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $firstSecret).Password
    $env:Bootstrap__SecondEmail = Read-Host 'Adresse e-mail du second membre'
    $secondSecret = Read-Host 'Mot de passe du second membre' -AsSecureString
    $env:Bootstrap__SecondPassword = [System.Net.NetworkCredential]::new('', $secondSecret).Password
    try {
        dotnet run --project $project --no-launch-profile -- --bootstrap --seed-demo
        if ($LASTEXITCODE -ne 0) { throw 'Initialisation échouée.' }
    } finally {
        Remove-Item Env:Bootstrap__Password,Env:Bootstrap__SecondPassword -ErrorAction SilentlyContinue
        $firstSecret.Dispose()
        $secondSecret.Dispose()
    }
}
dotnet run --project $project --no-launch-profile --urls http://localhost:5100
