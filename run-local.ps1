# Starts the API (on SQL Server LocalDB) and the Vite UI for local development.
# Usage: .\run-local.ps1            -> API http://localhost:5090/swagger, UI http://localhost:5190
#        .\run-local.ps1 -WithAgent        (also start the Python agent on :8000 instead of the stub)
#        .\run-local.ps1 -AgentUrl http://localhost:8000   (use an agent you started yourself)
# Real AI: copy src\agent\.env.example to src\agent\.env and fill it in. When that file exists the Python agent is
# started automatically with its settings (Azure OpenAI), so matching uses the model instead of the keyword stub.
# .env is git-ignored and docker-ignored: keys never enter the repository or the image.
param(
    [string]$ConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=TransferCredit;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True",
    [string]$AgentUrl = "",
    [switch]$WithAgent
)

$root = $PSScriptRoot
sqllocaldb start MSSQLLocalDB | Out-Null

$api = Join-Path $root "src\api\TransferCredit.Api"
$ui = Join-Path $root "src\ui"
$agent = Join-Path $root "src\agent"

if (-not (Test-Path (Join-Path $ui "node_modules"))) { Push-Location $ui; npm install; Pop-Location }

$agentEnv = Join-Path $agent ".env"
if ((Test-Path $agentEnv) -and -not $AgentUrl) {
    # KEY=value lines; the started windows inherit them.
    foreach ($line in Get-Content $agentEnv) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') { Set-Item "env:$($Matches[1])" $Matches[2] }
    }
    $WithAgent = $true
}

if ($WithAgent) {
    $venvPython = Join-Path $agent ".venv\Scripts\python.exe"
    if (-not (Test-Path $venvPython)) {
        Push-Location $agent
        python -m venv .venv
        # truststore: use the Windows certificate store (needed behind TLS-inspecting corporate proxies).
        & $venvPython -m pip install --use-feature=truststore -r requirements.txt -r requirements-dev.txt
        Pop-Location
    }
    Start-Process powershell -WorkingDirectory $agent -ArgumentList "-NoExit", "-Command", ".\.venv\Scripts\python.exe -m uvicorn app.main:app --port 8000"
    $AgentUrl = "http://localhost:8000"
}

# Environment variables override appsettings.Development.json. To use Azure SQL locally, pass -ConnectionString.
$apiCmd = "`$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:ConnectionStrings__Default='$ConnectionString'; `$env:AGENT_BASE_URL='$AgentUrl'; dotnet run --no-launch-profile --urls http://localhost:5090"
Start-Process powershell -WorkingDirectory $api -ArgumentList "-NoExit", "-Command", $apiCmd
Start-Process powershell -WorkingDirectory $ui -ArgumentList "-NoExit", "-Command", "npm run dev"

Write-Host "API: http://localhost:5090/swagger  (http://localhost:5090/ serves the last 'npm run build' of the UI)"
Write-Host "UI:  http://localhost:5190  (pick a demo user top-right)"
if ($AgentUrl) {
    $mode = if ($env:AZURE_OPENAI_ENDPOINT) { "Azure OpenAI ($env:AZURE_OPENAI_CHAT_DEPLOYMENT)" } else { "mock keyword ranking, not AI" }
    Write-Host "Agent: $AgentUrl/health  ($mode)"
}
