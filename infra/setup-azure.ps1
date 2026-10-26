# One-time Azure setup for the pipeline (deployment guide sections 4, 4.1 and 9). Safe to re-run.
# Creates: resource group, ACR, Container Apps environment, Key Vault, Blob storage, and per environment
# an internal agent app and a public API app with managed identities, ACR pull, Key Vault and Blob roles.
#
# Usage (after az login):
#   .\infra\setup-azure.ps1 -Suffix evk01 -SqlConnectionString "<Azure SQL connection string>"
# The connection string goes to Key Vault secret sql-conn only; it is never written to the repository.
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]{3,10}$')][string]$Suffix,
    [Parameter(Mandatory)][string]$SqlConnectionString,
    [string]$ResourceGroup = "rg-transfercredit",
    [string]$Location = "eastus",
    [string[]]$Environments = @("dev", "demo")
)
$ErrorActionPreference = "Stop"

function Az {
    # Runs az, throws on failure, returns trimmed output.
    $out = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed" }
    if ($out) { ($out -join "`n").Trim() }
}
function Retry([scriptblock]$Block, [int]$Times = 8) {
    # Role assignments take a minute or two to propagate.
    for ($i = 1; ; $i++) {
        try { return & $Block } catch { if ($i -ge $Times) { throw }; Write-Host "  retrying in 20s ($i/$Times)"; Start-Sleep 20 }
    }
}
function Grant($PrincipalId, $Role, $Scope, $Type = "ServicePrincipal") {
    $existing = Az role assignment list --assignee $PrincipalId --role $Role --scope $Scope --query "length(@)" -o tsv
    if ($existing -eq "0") {
        Retry { Az role assignment create --assignee-object-id $PrincipalId --assignee-principal-type $Type --role $Role --scope $Scope -o none }
    }
}

$acr = "acrtransfercredit$Suffix"
$kv = "kv-transfercredit-$Suffix"
$sa = "sttransfercredit$Suffix"
$cae = "cae-transfercredit"

Write-Host "Providers"
foreach ($p in "Microsoft.App", "Microsoft.OperationalInsights", "Microsoft.ContainerRegistry", "Microsoft.KeyVault", "Microsoft.Storage") {
    Az provider register -n $p --wait -o none
}
Az extension add --name containerapp --upgrade --only-show-errors -o none

Write-Host "Shared resources in $ResourceGroup ($Location)"
Az group create -n $ResourceGroup -l $Location -o none
Az acr create -n $acr -g $ResourceGroup --sku Basic -o none
if (-not (Az containerapp env list -g $ResourceGroup --query "[?name=='$cae'].name" -o tsv)) {
    Az containerapp env create -n $cae -g $ResourceGroup -l $Location -o none
}
if (-not (Az keyvault list -g $ResourceGroup --query "[?name=='$kv'].name" -o tsv)) {
    Az keyvault create -n $kv -g $ResourceGroup -l $Location --enable-rbac-authorization true -o none
}
Az storage account create -n $sa -g $ResourceGroup -l $Location --sku Standard_LRS `
    --allow-blob-public-access false --min-tls-version TLS1_2 -o none

$acrId = Az acr show -n $acr --query id -o tsv
$kvId = Az keyvault show -n $kv --query id -o tsv
$saId = Az storage account show -n $sa -g $ResourceGroup --query id -o tsv
$me = Az ad signed-in-user show --query id -o tsv

Write-Host "Key Vault secret sql-conn"
Grant $me "Key Vault Secrets Officer" $kvId "User"
Retry { Az keyvault secret set --vault-name $kv -n sql-conn --value $SqlConnectionString -o none }

Write-Host "Blob containers"
Grant $me "Storage Blob Data Contributor" $saId "User"
foreach ($c in "intake-raw", "documents", "extracted") {
    Retry { Az storage container create --account-name $sa -n $c --auth-mode login -o none }
}

$placeholder = "mcr.microsoft.com/k8se/quickstart:latest"
foreach ($envName in $Environments) {
    $agentApp = "ca-agent-$envName"
    $apiApp = "ca-api-$envName"
    Write-Host "Environment $envName ($agentApp, $apiApp)"

    # Agent: internal ingress only. API: public ingress. The first pipeline run replaces the placeholder image.
    foreach ($app in @(@{ n = $agentApp; ingress = "internal"; port = 8000 }, @{ n = $apiApp; ingress = "external"; port = 8080 })) {
        if (-not (Az containerapp list -g $ResourceGroup --query "[?name=='$($app.n)'].name" -o tsv)) {
            Az containerapp create -n $app.n -g $ResourceGroup --environment $cae --image $placeholder `
                --ingress $app.ingress --target-port $app.port --system-assigned --min-replicas 1 -o none
        }
        $principal = Az containerapp show -n $app.n -g $ResourceGroup --query identity.principalId -o tsv
        Grant $principal "AcrPull" $acrId
        Grant $principal "Key Vault Secrets User" $kvId
        Grant $principal ($(if ($app.n -eq $apiApp) { "Storage Blob Data Contributor" } else { "Storage Blob Data Reader" })) $saId
        Retry { Az containerapp registry set -n $app.n -g $ResourceGroup --server "$acr.azurecr.io" --identity system -o none }
    }

    Retry { Az containerapp secret set -n $apiApp -g $ResourceGroup `
        --secrets "sql-conn=keyvaultref:https://$kv.vault.azure.net/secrets/sql-conn,identityref:system" -o none }
    # ScanMode Simulated: switch to Defender after enabling Defender for Storage malware scanning (guide 4.1).
    Az containerapp update -n $apiApp -g $ResourceGroup --set-env-vars `
        "ConnectionStrings__Default=secretref:sql-conn" "ASPNETCORE_ENVIRONMENT=Production" `
        "STORAGE_ACCOUNT_URL=https://$sa.blob.core.windows.net" "Storage__ScanMode=Simulated" `
        "AGENT_BASE_URL=http://$agentApp" "INTAKE_ENABLED=false" -o none
    # The agent runs in mock mode until AZURE_OPENAI_ENDPOINT and the key are set (guide section 9).
}

Write-Host ""
Write-Host "Done. Set acrName: '$acr' in azure-pipelines.yml."
foreach ($envName in $Environments) {
    $fqdn = Az containerapp show -n "ca-api-$envName" -g $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv
    Write-Host "  $envName URL: https://$fqdn"
}
