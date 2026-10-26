# One-time Azure setup for the pipeline. Safe to re-run.
# Hosting: Azure App Service (Linux containers). It needs only Contributor on the resource group: no role
# assignments and no subscription-level provider registration, unlike the Container Apps design in the guide.
#
# Creates: ACR (admin credentials), one Linux App Service plan, and per environment an API app and an agent app.
# Each app runs the image tag named after its environment (:dev, :demo). An ACR webhook redeploys the app when the
# pipeline pushes that tag. The agent app accepts traffic only from the API app's outbound addresses.
#
# Usage (after az login and az account set):
#   .\infra\setup-azure.ps1 -SqlConnectionString "<Azure SQL connection string>"
# The connection string is stored as an App Service connection string only; it is never written to the repository.
param(
    [Parameter(Mandatory)][string]$SqlConnectionString,
    [ValidatePattern('^[a-z0-9]{3,10}$')][string]$Suffix = "btb",
    [string]$ResourceGroup = "rg-beyond-the-build",
    [string]$Location = "centralindia",
    [string]$PlanSku = "B2",
    [string[]]$Environments = @("dev", "demo")
)
$ErrorActionPreference = "Stop"

function Az {
    # Runs az, throws on failure, returns trimmed output.
    $out = & az.cmd @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed" }
    if ($out) { ($out -join "`n").Trim() }
}

$acr = "acrtransfercredit$Suffix"
$plan = "plan-transfercredit-$Suffix"
$registry = "$acr.azurecr.io"

Write-Host "Shared resources in $ResourceGroup ($Location)"
if (-not (Az acr list -g $ResourceGroup --query "[?name=='$acr'].name" -o tsv)) {
    Az acr create -n $acr -g $ResourceGroup -l $Location --sku Basic -o none
}
Az acr update -n $acr --admin-enabled true -o none
$acrUser = Az acr credential show -n $acr --query username -o tsv
$acrPassword = Az acr credential show -n $acr --query "passwords[0].value" -o tsv
if (-not (Az appservice plan list -g $ResourceGroup --query "[?name=='$plan'].name" -o tsv)) {
    Az appservice plan create -n $plan -g $ResourceGroup -l $Location --is-linux --sku $PlanSku -o none
}

foreach ($envName in $Environments) {
    $apiApp = "app-tc-api-$envName-$Suffix"
    $agentApp = "app-tc-agent-$envName-$Suffix"
    Write-Host "Environment $envName ($apiApp, $agentApp)"

    foreach ($app in @(@{ n = $agentApp; image = "transfercredit-agent" }, @{ n = $apiApp; image = "transfercredit-api" })) {
        $image = "$registry/$($app.image):$envName"
        if (-not (Az webapp list -g $ResourceGroup --query "[?name=='$($app.n)'].name" -o tsv)) {
            # The image does not exist until the first pipeline run; the app starts once the webhook fires.
            Az webapp create -n $app.n -g $ResourceGroup -p $plan --container-image-name $image `
                --container-registry-url "https://$registry" --container-registry-user $acrUser `
                --container-registry-password $acrPassword -o none
        }
        Az webapp config container set -n $app.n -g $ResourceGroup --container-image-name $image `
            --container-registry-url "https://$registry" --container-registry-user $acrUser `
            --container-registry-password $acrPassword -o none
        Az webapp config set -n $app.n -g $ResourceGroup --always-on true --http20-enabled true --min-tls-version 1.2 -o none
        Az webapp update -n $app.n -g $ResourceGroup --https-only true -o none

        # The continuous-deployment webhook URL carries the app's publishing credentials, so SCM basic auth must be on.
        Az resource update -g $ResourceGroup --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies `
            --name scm --parent "sites/$($app.n)" --set properties.allow=true -o none
        $hook = Az webapp deployment container config -n $app.n -g $ResourceGroup --enable-cd true --query CI_CD_URL -o tsv
        $hookName = ($app.n -replace '[^a-zA-Z0-9]', '')
        if ($hookName.Length -gt 50) { $hookName = $hookName.Substring(0, 50) }
        if (Az acr webhook list -r $acr --query "[?name=='$hookName'].name" -o tsv) {
            Az acr webhook update -r $acr -n $hookName --uri $hook --actions push --scope "$($app.image):$envName" -o none
        } else {
            Az acr webhook create -r $acr -n $hookName --uri $hook --actions push --scope "$($app.image):$envName" -o none
        }
    }

    Az webapp config appsettings set -n $agentApp -g $ResourceGroup -o none --settings "WEBSITES_PORT=8000" `
        "WEBSITES_ENABLE_APP_SERVICE_STORAGE=false"
    # The agent runs in mock mode until AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY are set (guide section 9).

    # /home is persistent App Service storage, so uploaded documents survive restarts. ScanMode Simulated: there is
    # no Defender for Storage here.
    Az webapp config appsettings set -n $apiApp -g $ResourceGroup -o none --settings "WEBSITES_PORT=8080" `
        "WEBSITES_ENABLE_APP_SERVICE_STORAGE=true" "ASPNETCORE_ENVIRONMENT=Production" `
        "AGENT_BASE_URL=https://$agentApp.azurewebsites.net" "Storage__LocalRoot=/home/data/storage" `
        "Storage__ScanMode=Simulated" "INTAKE_ENABLED=false"
    Az webapp config connection-string set -n $apiApp -g $ResourceGroup -o none -t SQLAzure `
        --settings "Default=$SqlConnectionString"

    # Agent: allow only the API app's outbound addresses (az accepts up to 8 addresses per rule).
    $ips = (Az webapp show -n $apiApp -g $ResourceGroup --query possibleOutboundIpAddresses -o tsv) -split ',' | Sort-Object -Unique
    $existing = Az webapp config access-restriction show -n $agentApp -g $ResourceGroup --query "ipSecurityRestrictions[?starts_with(name,'api-')].name" -o tsv
    foreach ($rule in ($existing -split "`n" | Where-Object { $_ })) {
        Az webapp config access-restriction remove -n $agentApp -g $ResourceGroup --rule-name $rule -o none
    }
    for ($i = 0; $i -lt $ips.Count; $i += 8) {
        $chunk = ($ips[$i..([Math]::Min($i + 7, $ips.Count - 1))] | ForEach-Object { "$_/32" }) -join ','
        Az webapp config access-restriction add -n $agentApp -g $ResourceGroup --rule-name "api-$($i / 8)" `
            --action Allow --ip-address $chunk --priority (100 + $i / 8) -o none
    }
}

Write-Host ""
Write-Host "Done. Registry: $registry (user $acrUser)."
foreach ($envName in $Environments) { Write-Host "  $envName URL: https://app-tc-api-$envName-$Suffix.azurewebsites.net" }
