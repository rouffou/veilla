<#
.SYNOPSIS
  Démo locale en une commande (#393) : repart d'un environnement vierge, attend les services, puis amorce les données fictives.

.DESCRIPTION
  1. docker compose down -v      EFFACE les volumes du projet : bases PostgreSQL, Keycloak, documents (confirmation demandée)
  2. docker compose up -d --build
  3. attente de tous les /health/ready
  4. dotnet run tools/donnees-demo : comptes, affiliés, travailleurs, rendez-vous, décisions, manifeste et résumé

  Poste de développement uniquement : l'outil utilise le flux mot de passe de veilla-dev-cli et l'API d'administration de Keycloak.

.PARAMETER Oui
  Saute la confirmation (scripts, intégration continue).

.PARAMETER Projet
  Nom du projet compose (défaut : « veilla », celui de compose.yaml) ; permet un essai sur un projet distinct.

.PARAMETER SeulementAmorcage
  N'efface ni ne redémarre rien : relance seulement l'outil de données (idempotent).

.PARAMETER Delai
  Délai d'attente maximal des services, en secondes (défaut : 900).
#>
[CmdletBinding()]
param(
    [switch]$Oui,
    [string]$Projet = '',
    [switch]$SeulementAmorcage,
    [int]$Delai = 900
)

$ErrorActionPreference = 'Stop'
$racine = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $racine

$compose = @('compose', '-f', 'deploy/local/compose.yaml')
$nomProjet = 'veilla'
if ($Projet) {
    $compose += @('-p', $Projet)
    $nomProjet = $Projet
}

function Invoke-Docker {
    param([string[]]$Arguments)
    & docker @compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($Arguments -join ' ') a échoué (code $LASTEXITCODE)." }
}

if (-not $SeulementAmorcage) {
    if (-not $Oui) {
        Write-Host "ATTENTION : cette commande exécute « docker compose down -v » sur le projet « $nomProjet »." -ForegroundColor Yellow
        Write-Host 'Toutes les données locales de ce projet seront EFFACÉES : bases PostgreSQL, utilisateurs Keycloak, documents.' -ForegroundColor Yellow
        if (-not [Environment]::UserInteractive -or [Console]::IsInputRedirected) {
            Write-Error 'Pas de terminal interactif : relancez avec -Oui pour confirmer.'
            exit 1
        }
        $reponse = Read-Host 'Tapez OUI pour continuer'
        if ($reponse -cne 'OUI') {
            Write-Host 'Abandon, rien n''a été modifié.'
            exit 1
        }
    }

    Write-Host "== Arrêt et effacement du projet « $nomProjet »"
    Invoke-Docker @('down', '-v', '--remove-orphans')
    Write-Host '== Construction et démarrage'
    Invoke-Docker @('--progress', 'quiet', 'up', '-d', '--build')

    Write-Host "== Attente des services (délai maximal $Delai s)"
    $urls = [ordered]@{
        'Keycloak'              = 'http://localhost:8180/realms/veilla/.well-known/openid-configuration'
        'Référentiels'          = 'http://localhost:5110/health/ready'
        'Affiliés'              = 'http://localhost:5111/health/ready'
        'Personnes'             = 'http://localhost:5112/health/ready'
        'Postes et risques'     = 'http://localhost:5113/health/ready'
        'Audit'                 = 'http://localhost:5114/health/ready'
        'Intégrations'          = 'http://localhost:5115/health/ready'
        'Obligations'           = 'http://localhost:5116/health/ready'
        'Planification'         = 'http://localhost:5117/health/ready'
        'Surveillance médicale' = 'http://localhost:5118/health/ready'
        'Documents'             = 'http://localhost:5119/health/ready'
        'Communications'        = 'http://localhost:5120/health/ready'
        'Réintégration'         = 'http://localhost:5121/health/ready'
        'BFF employeur'         = 'http://localhost:5200/health/ready'
        'BFF travailleur'       = 'http://localhost:5201/health/ready'
    }
    $services = & docker @compose config --services
    if ($services -contains 'bff-interne') { $urls['BFF interne'] = 'http://localhost:5100/health/ready' }
    $limite = (Get-Date).AddSeconds($Delai)
    foreach ($nom in $urls.Keys) {
        while ($true) {
            try {
                $null = Invoke-WebRequest -Uri $urls[$nom] -UseBasicParsing -TimeoutSec 5
                break
            } catch {
                if ((Get-Date) -ge $limite) {
                    Write-Error "Délai dépassé : $nom ne répond pas sur $($urls[$nom]). Voir « docker $($compose -join ' ') logs »."
                    exit 2
                }
                Start-Sleep -Seconds 3
            }
        }
        Write-Host "   prêt : $nom"
    }
}

Write-Host '== Amorçage des données de démonstration'
& dotnet run --project tools/donnees-demo/Sepp.Outils.DonneesDemo -c Release -- --racine $racine
exit $LASTEXITCODE
