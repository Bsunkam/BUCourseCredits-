# AZURE DEVOPS CI/CD DEPLOYMENT GUIDE

**React • ASP.NET Core 8 API • Python (FastAPI + LangGraph) agent service | Azure Repos • Azure Container Apps**

**Document purpose:** a step-by-step guide to build, test, package and deploy the three parts of the Transfer Credit solution from Azure Repos using a multi-stage Azure Pipelines YAML pipeline.

This replaces the earlier single-app guide (ASP.NET Core MVC on Windows App Service). The application is now a React front end served by an ASP.NET Core 8 API, plus a private Python agent service, all deployed as containers.

## 1. Scope and assumptions

- **Source control:** Azure Repos Git. **Trigger branch:** main.
- **Components:** React (Vite) front end, ASP.NET Core 8 API (serves the built React app), Python 3.11 FastAPI agent service.
- **Build agent:** Microsoft-hosted Linux agent (ubuntu-latest). This changes from the earlier windows-latest because Docker builds and Python tooling run on Linux.
- **Registry:** Azure Container Registry (ACR).
- **Storage:** Azure Blob Storage (containers intake-raw, documents, extracted) with Defender for Storage malware scanning.
- **Email intake:** a dedicated intake mailbox read through Microsoft Graph with Mail.Read, scoped by Exchange RBAC for Applications.
- **Deployment target:** Azure Container Apps. The API has public ingress. The agent service has internal ingress only.
- **Environments:** dev (automatic) and demo (after approval).
- **Pipeline type:** YAML, multi-stage, with a deployment template.
- **Testing:** xUnit (.NET), pytest (Python), npm tests and lint (React), Playwright end-to-end on dev.

## 2. Deployment architecture

![pipe](images/pipe.png)

*Figure 1. The five pipeline stages.*

1. A developer pushes code to the main branch in Azure Repos. Pull requests run Stage 1 only, through a branch policy.
2. **Stage 1, Build and test:** three parallel jobs build and test the .NET solution, the React app and the Python agent service.
3. **Stage 2, Package:** Docker images for the API (with the React build inside) and the agent service are built and pushed to ACR, tagged with the build ID.
4. **Stage 3, Deploy Dev:** the agent service is updated first, then the API. A smoke test checks both, including API-to-agent connectivity, and Playwright runs against the dev URL.
5. **Stage 4, Approval gate:** an approval check on the demo environment pauses the pipeline for a named approver.
6. **Stage 5, Deploy Demo:** the same images, with the same tag, are deployed and smoke tested. Nothing is rebuilt between environments.
7. The team verifies the deployed URL, logs and key workflows.

## 3. Repository layout and prerequisites

Adjust the paths in the pipeline if your repository differs.

```
/src/TransferCredit.Api/TransferCredit.Api.csproj
/web                      # React (Vite): package.json, src/
/agent                    # FastAPI + LangGraph
  requirements.txt  requirements-dev.txt  Dockerfile
  /app  /graph  /tests  /eval
/tests/e2e                # Playwright
/docker/api.Dockerfile
/pipelines/deploy.yml     # deployment template
azure-pipelines.yml
TransferCredit.sln
```

- An Azure DevOps project with the source in Azure Repos.
- A solution file, the API project, test projects (names ending in Tests.csproj), the web folder and the agent folder.
- An Azure subscription with permission to create a resource group, ACR, a Container Apps environment and Key Vault.
- Azure DevOps permission to create pipelines, service connections and environments.
- The API must expose GET /health, GET /api/health/agent and GET /api/health/intake. The agent service must expose GET /health. The smoke test depends on these.
- An intake mailbox (a test mailbox for the hackathon) and an Exchange administrator who can set the mailbox scope.
- The API must serve static files and fall back to index.html for React routes (UseStaticFiles and MapFallbackToFile).

## 4. One-time Azure setup

Create these resources once, before the first pipeline run. Replace the sample names. The commands use the Azure CLI.

```
RG=rg-transfercredit
LOC=eastus
ACR=yourregistry            # globally unique, lowercase
ENV=cae-transfercredit
KV=kv-transfercredit

az group create -n $RG -l $LOC
az acr create -n $ACR -g $RG --sku Basic
az containerapp env create -n $ENV -g $RG -l $LOC
az keyvault create -n $KV -g $RG -l $LOC

# Agent service: internal ingress only (not reachable from the internet)
az containerapp create -n ca-agent-dev -g $RG --environment $ENV \
  --image mcr.microsoft.com/k8se/quickstart:latest \
  --ingress internal --target-port 8000 --system-assigned

# API: public ingress
az containerapp create -n ca-api-dev -g $RG --environment $ENV \
  --image mcr.microsoft.com/k8se/quickstart:latest \
  --ingress external --target-port 8080 --system-assigned
```

The quickstart image is a placeholder. The first pipeline run replaces it with your image. Repeat the two create commands with demo names (ca-agent-demo, ca-api-demo) for the demo environment.

### Let the apps pull from ACR and read Key Vault

```
ACR_ID=$(az acr show -n $ACR --query id -o tsv)
for APP in ca-agent-dev ca-api-dev; do
  PID=$(az containerapp show -n $APP -g $RG --query identity.principalId -o tsv)
  az role assignment create --assignee $PID --role AcrPull --scope $ACR_ID
  az containerapp registry set -n $APP -g $RG \
    --server $ACR.azurecr.io --identity system
done
# Also grant each app "Key Vault Secrets User" on the vault (section 9).
```

### 4.1 Blob Storage for documents

One storage account holds all uploaded and emailed files. Public access is off, and the apps use their managed identities, not account keys.

```
SA=yourstorage              # 3-24 lowercase letters and digits, globally unique

az storage account create -n $SA -g $RG -l $LOC --sku Standard_LRS \
  --allow-blob-public-access false --min-tls-version TLS1_2

for C in intake-raw documents extracted; do
  az storage container create --account-name $SA -n $C --auth-mode login
done

SA_ID=$(az storage account show -n $SA -g $RG --query id -o tsv)
API_PID=$(az containerapp show -n ca-api-dev -g $RG --query identity.principalId -o tsv)
AGENT_PID=$(az containerapp show -n ca-agent-dev -g $RG --query identity.principalId -o tsv)

az role assignment create --assignee $API_PID --scope $SA_ID \
  --role "Storage Blob Data Contributor"
az role assignment create --assignee $AGENT_PID --scope $SA_ID \
  --role "Storage Blob Data Reader"
```

- **Malware scanning:** in the Azure portal, open Microsoft Defender for Cloud and enable Defender for Storage with on-upload malware scanning for this storage account. Set a monthly scanning cap to control cost. Scan results are written to the blob's index tags.
- **Retention:** add a lifecycle management rule to delete intake-raw blobs after the retention period agreed with the sponsor.
- **Blob URL format:** https://<storage-account>.blob.core.windows.net/<container>/<path>. It opens only for an identity with a role, or with a SAS token issued by the API.
- Repeat the storage setup for the demo environment, or use separate containers with a prefix per environment.

### 4.2 Mailbox access through Microsoft Graph

The API reads one intake mailbox with the Mail.Read application permission. Access is limited to that mailbox through Exchange Online RBAC for Applications. **Do not grant Mail.Read in Entra ID**: a tenant-wide Graph permission bypasses the Exchange scope and would let the app read every mailbox.

1. In the Exchange admin center, create a shared mailbox, for example transfer-intake@yourdomain. For the hackathon use a test mailbox with synthetic emails only.
2. Find the identity of the API container app: its object ID from az containerapp show, and its application (client) ID from the Enterprise applications page or az ad sp show.
3. An Exchange administrator runs the commands below in Exchange Online PowerShell.
4. Test the result, then confirm that the app can read the intake mailbox and no other mailbox.

```
Connect-ExchangeOnline

# Register the API identity in Exchange (use the IDs from step 2)
New-ServicePrincipal -AppId <app-client-id> -ObjectId <object-id> `
  -DisplayName "TransferCredit API"

# Limit the scope to the intake mailbox only
New-ManagementScope -Name "TransferCredit-Intake" `
  -RecipientRestrictionFilter "PrimarySmtpAddress -eq 'transfer-intake@yourdomain'"

# Grant read-only mail access within that scope
New-ManagementRoleAssignment -App <object-id> -Role "Application Mail.Read" `
  -CustomResourceScope "TransferCredit-Intake"

# Check
Test-ServicePrincipalAuthorization -Identity <object-id> `
  -Resource transfer-intake@yourdomain
```

> **Check the commands.** Cmdlet names and parameters can change. Confirm them against the Microsoft page before running: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac

> **Fallback.** If the mailbox scope cannot be set in time, turn off live email intake (INTAKE_ENABLED=false) and use the email simulator page that imports .eml files. The portal upload and the rest of the pipeline are unaffected.

> **Security note.** Do not commit passwords, client secrets, access tokens or production credentials to the repository or the YAML file. Use managed identity and Key Vault.

## 5. Azure DevOps service connections and environments

### 5.1 Azure Resource Manager connection

1. Open Project settings, then Service connections.
2. Select New service connection and choose Azure Resource Manager.
3. Use the authentication method your organisation approves; workload identity federation is preferred where supported.
4. Select the subscription and scope the connection to the resource group rg-transfercredit.
5. Name it sc-azure-rm and authorise the pipeline to use it when prompted.

### 5.2 Container registry connection

1. Add a new service connection of type Docker Registry, then Azure Container Registry.
2. Select your subscription and the registry, and name it sc-acr.

### 5.3 Environments and approvals

1. Open Pipelines, then Environments, and create two environments: tc-dev and tc-demo.
2. On tc-demo, open the menu, choose Approvals and checks, and add an Approval with one or more named approvers.
3. Leave tc-dev without approvals so Stage 3 runs automatically.

## 6. Containerise the services

### 6.1 API image (docker/api.Dockerfile, build context = repository root)

A multi-stage build compiles the React app, publishes the API and copies the React build into wwwroot, so one container serves both.

```
FROM node:20-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY src/ ./src/
RUN dotnet publish src/TransferCredit.Api/TransferCredit.Api.csproj \
    -c Release -o /app/publish
COPY --from=web /web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TransferCredit.Api.dll"]
```

### 6.2 Agent image (agent/Dockerfile, build context = agent folder)

```
FROM python:3.11-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

Add a .dockerignore to each context (node_modules, bin, obj, .venv, __pycache__, .git) so builds stay small and secrets or local files never enter an image.

## 7. Add the pipeline files to the repository

Create azure-pipelines.yml in the repository root and pipelines/deploy.yml for the shared deployment steps. Replace all sample names with your own.

### 7.1 azure-pipelines.yml

```
trigger:
  branches:
    include:
      - main

# Azure Repos pull-request validation is set under
# Repos > Branches > main > Branch policies > Build validation.

variables:
  buildConfiguration: 'Release'
  dotnetVersion: '8.0.x'
  nodeVersion: '20.x'
  pythonVersion: '3.11'
  solutionPath: 'TransferCredit.sln'
  testProjects: 'tests/**/*Tests.csproj'
  azureServiceConnection: 'sc-azure-rm'
  acrServiceConnection: 'sc-acr'
  acrName: 'yourregistry'
  apiImage: 'transfercredit-api'
  agentImage: 'transfercredit-agent'
  resourceGroup: 'rg-transfercredit'

stages:

  # ---------- Stage 1: build and test (also used for PR validation) ----------
  - stage: Build
    displayName: 'Build and test'
    jobs:
      - job: DotNet
        displayName: '.NET build and test'
        pool: { vmImage: 'ubuntu-latest' }
        steps:
          - task: UseDotNet@2
            inputs: { packageType: 'sdk', version: '$(dotnetVersion)' }
          - task: DotNetCoreCLI@2
            displayName: 'Restore'
            inputs: { command: 'restore', projects: '$(solutionPath)' }
          - task: DotNetCoreCLI@2
            displayName: 'Build'
            inputs:
              command: 'build'
              projects: '$(solutionPath)'
              arguments: '--configuration $(buildConfiguration) --no-restore'
          - task: DotNetCoreCLI@2
            displayName: 'xUnit tests'
            inputs:
              command: 'test'
              projects: '$(testProjects)'
              arguments: >
                --configuration $(buildConfiguration)
                --no-build --no-restore
              publishTestResults: true

      - job: Web
        displayName: 'React build and test'
        pool: { vmImage: 'ubuntu-latest' }
        steps:
          - task: NodeTool@0
            inputs: { versionSpec: '$(nodeVersion)' }
          - script: npm ci
            workingDirectory: web
            displayName: 'Install packages'
          - script: npm run lint --if-present
            workingDirectory: web
            displayName: 'Lint'
          - script: npm test --if-present
            workingDirectory: web
            displayName: 'Unit tests'
          - script: npm run build
            workingDirectory: web
            displayName: 'Production build'

      - job: Agent
        displayName: 'Python agent tests'
        pool: { vmImage: 'ubuntu-latest' }
        steps:
          - task: UsePythonVersion@0
            inputs: { versionSpec: '$(pythonVersion)' }
          - script: |
              python -m pip install --upgrade pip
              pip install -r agent/requirements.txt -r agent/requirements-dev.txt
            displayName: 'Install dependencies'
          - script: |
              pytest agent/tests \
                --junitxml=$(Build.ArtifactStagingDirectory)/agent-tests.xml
            displayName: 'pytest (model calls mocked)'
          - task: PublishTestResults@2
            condition: succeededOrFailed()
            inputs:
              testResultsFormat: 'JUnit'
              testResultsFiles: '$(Build.ArtifactStagingDirectory)/agent-tests.xml'

  # ---------- Stage 2: build and push images ----------
  - stage: Package
    displayName: 'Package images'
    dependsOn: Build
    condition: and(succeeded(), ne(variables['Build.Reason'], 'PullRequest'))
    jobs:
      - job: Images
        pool: { vmImage: 'ubuntu-latest' }
        steps:
          - task: Docker@2
            displayName: 'Build and push API image'
            inputs:
              containerRegistry: '$(acrServiceConnection)'
              repository: '$(apiImage)'
              command: 'buildAndPush'
              Dockerfile: 'docker/api.Dockerfile'
              buildContext: '$(Build.SourcesDirectory)'
              tags: '$(Build.BuildId)'
          - task: Docker@2
            displayName: 'Build and push agent image'
            inputs:
              containerRegistry: '$(acrServiceConnection)'
              repository: '$(agentImage)'
              command: 'buildAndPush'
              Dockerfile: 'agent/Dockerfile'
              buildContext: 'agent'
              tags: '$(Build.BuildId)'

  # ---------- Stage 3: deploy to dev (automatic) ----------
  - stage: DeployDev
    displayName: 'Deploy to Dev'
    dependsOn: Package
    condition: succeeded()
    jobs:
      - deployment: Dev
        environment: 'tc-dev'
        pool: { vmImage: 'ubuntu-latest' }
        strategy:
          runOnce:
            deploy:
              steps:
                - template: pipelines/deploy.yml
                  parameters:
                    apiApp: 'ca-api-dev'
                    agentApp: 'ca-agent-dev'
                    runE2E: true

  # ---------- Stages 4 and 5: approval, then deploy to demo ----------
  - stage: DeployDemo
    displayName: 'Deploy to Demo (approval required)'
    dependsOn: DeployDev
    condition: succeeded()
    jobs:
      - deployment: Demo
        environment: 'tc-demo'   # approval check lives on this environment
        pool: { vmImage: 'ubuntu-latest' }
        strategy:
          runOnce:
            deploy:
              steps:
                - template: pipelines/deploy.yml
                  parameters:
                    apiApp: 'ca-api-demo'
                    agentApp: 'ca-agent-demo'
                    runE2E: false
```

Stage 4 has no YAML of its own. The approval is an environment check, so the pipeline pauses automatically when Stage 5 starts and waits for the approver.

### 7.2 pipelines/deploy.yml (shared deployment steps)

```
parameters:
  - name: apiApp
    type: string
  - name: agentApp
    type: string
  - name: runE2E
    type: boolean
    default: false

steps:
  - checkout: self

  # Agent first, so the API never points at an older agent contract
  - task: AzureCLI@2
    displayName: 'Deploy agent service'
    inputs:
      azureSubscription: '$(azureServiceConnection)'
      scriptType: 'bash'
      scriptLocation: 'inlineScript'
      inlineScript: |
        az containerapp update -n ${{ parameters.agentApp }} -g $(resourceGroup) \
          --image $(acrName).azurecr.io/$(agentImage):$(Build.BuildId)

  - task: AzureCLI@2
    displayName: 'Deploy API (with React build)'
    inputs:
      azureSubscription: '$(azureServiceConnection)'
      scriptType: 'bash'
      scriptLocation: 'inlineScript'
      inlineScript: |
        az containerapp update -n ${{ parameters.apiApp }} -g $(resourceGroup) \
          --image $(acrName).azurecr.io/$(apiImage):$(Build.BuildId) \
          --set-env-vars "AGENT_BASE_URL=http://${{ parameters.agentApp }}"

  - task: AzureCLI@2
    displayName: 'Smoke test'
    inputs:
      azureSubscription: '$(azureServiceConnection)'
      scriptType: 'bash'
      scriptLocation: 'inlineScript'
      inlineScript: |
        FQDN=$(az containerapp show -n ${{ parameters.apiApp }} \
          -g $(resourceGroup) \
          --query properties.configuration.ingress.fqdn -o tsv)
        check() {
          for i in $(seq 1 12); do
            code=$(curl -s -o /dev/null -w "%{http_code}" "https://$FQDN$1" || true)
            [ "$code" = "200" ] && return 0
            sleep 15
          done
          echo "FAILED: $1 returned $code"; return 1
        }
        check /health
        check /api/health/agent   # proves API can reach the private agent
        check /api/health/intake  # Blob reachable; mailbox reachable or disabled
        echo "##vso[task.setvariable variable=apiFqdn]$FQDN"

  - ${{ if eq(parameters.runE2E, true) }}:
    - task: NodeTool@0
      inputs: { versionSpec: '$(nodeVersion)' }
    - script: |
        npm ci
        npx playwright install --with-deps chromium
        BASE_URL=https://$(apiFqdn) npx playwright test --reporter=junit
      workingDirectory: tests/e2e
      displayName: 'Playwright end-to-end (six-course demo flow)'
      env:
        PLAYWRIGHT_JUNIT_OUTPUT_NAME: $(Build.ArtifactStagingDirectory)/e2e.xml
    - task: PublishTestResults@2
      condition: succeededOrFailed()
      inputs:
        testResultsFormat: 'JUnit'
        testResultsFiles: '$(Build.ArtifactStagingDirectory)/e2e.xml'
```

> **Why this order.** The API is deployed after the agent service so a new API never calls an older agent. The smoke test checks /api/health/agent, which fails if the private agent service is unreachable, so a broken internal link is caught before any approver sees the demo stage.

## 8. Customise the pipeline variables

| Variable | Sample value | What to configure |
| --- | --- | --- |
| solutionPath | TransferCredit.sln | Relative path to the solution file. |
| testProjects | tests/**/*Tests.csproj | Glob matching your test project filenames. If nothing matches the test step fails; fix the pattern rather than ignoring it. |
| azureServiceConnection | sc-azure-rm | Exact name of the Azure Resource Manager connection. |
| acrServiceConnection | sc-acr | Exact name of the Docker Registry connection. |
| acrName | yourregistry | ACR name without .azurecr.io. |
| apiImage / agentImage | transfercredit-api / transfercredit-agent | Repository names in ACR. |
| resourceGroup | rg-transfercredit | Resource group holding the container apps. |
| apiApp / agentApp (parameters) | ca-api-dev / ca-agent-dev | Container app names for each environment. |
| dotnetVersion / nodeVersion / pythonVersion | 8.0.x / 20.x / 3.11 | Tool versions used by the build jobs and Dockerfiles. Keep them aligned. |

## 9. Configure app settings and secrets

- **Never** put keys in the repository, the YAML or appsettings.json.
- Store secrets in Key Vault: the Azure SQL connection string, Blob connection or identity settings, and the Azure OpenAI, Document Intelligence and AI Search keys.
- Give each container app's managed identity the Key Vault Secrets User role, then reference secrets from the app.

```
VAULT=https://$KV.vault.azure.net/secrets

# API: SQL connection string from Key Vault
az containerapp secret set -n ca-api-dev -g $RG \
  --secrets "sql-conn=keyvaultref:$VAULT/sql-conn,identityref:system"
az containerapp update -n ca-api-dev -g $RG \
  --set-env-vars "ConnectionStrings__Default=secretref:sql-conn" \
                 "ASPNETCORE_ENVIRONMENT=Production"

# Agent service: Azure OpenAI key from Key Vault
az containerapp secret set -n ca-agent-dev -g $RG \
  --secrets "aoai-key=keyvaultref:$VAULT/aoai-key,identityref:system"
az containerapp update -n ca-agent-dev -g $RG \
  --set-env-vars "AZURE_OPENAI_API_KEY=secretref:aoai-key" \
    "AZURE_OPENAI_ENDPOINT=https://<your-resource>.openai.azure.com/"
```

### Document intake settings

```
az containerapp update -n ca-api-dev -g $RG --set-env-vars \
  "STORAGE_ACCOUNT_URL=https://$SA.blob.core.windows.net" \
  "INTAKE_ENABLED=true" \
  "INTAKE_MAILBOX=transfer-intake@yourdomain" \
  "INTAKE_POLL_SECONDS=90" \
  "MAX_UPLOAD_MB=20" \
  "ALLOWED_FILE_TYPES=pdf,docx,png,jpg"
```

None of these are secrets, because access uses managed identity. If you use an app registration instead, store its client secret in Key Vault and reference it with secretref, as above.

Pipeline runs do not overwrite these settings. The deploy step only changes the image and the AGENT_BASE_URL variable. Also confirm that Azure SQL firewall and network rules allow the API's outbound traffic.

## 10. Create and run the pipeline

1. Commit azure-pipelines.yml and pipelines/deploy.yml to the main branch.
2. In Azure DevOps open Pipelines, then New pipeline.
3. Choose Azure Repos Git and select the repository.
4. Choose Existing Azure Pipelines YAML file, select branch main and /azure-pipelines.yml.
5. Review and save the pipeline.
6. Authorise the service connections and environments when prompted.
7. Run the pipeline and read the logs for each stage.
8. When Stage 5 pauses, the approver opens the run and approves the demo deployment.

## 11. Pull-request validation for Azure Repos

The YAML trigger starts a build when code is pushed to main. For Azure Repos Git, pull-request builds are set with branch policies. Because Stages 2 to 5 are skipped for pull requests, a PR runs only build and test.

1. Open Repos, then Branches.
2. Open the menu for main and select Branch policies.
3. Under Build validation, add this pipeline.
4. Set the policy to Required, and save.

## 12. Validate deployment

| Check | Expected result |
| --- | --- |
| .NET restore, build, xUnit | Packages restore, the solution compiles, tests pass and results are published. |
| React lint, tests, build | No lint errors; unit tests pass; the production build succeeds. |
| pytest | Agent tests pass with model calls mocked; results published. |
| Package | Both images appear in ACR with the build ID tag. |
| Deploy | az containerapp update succeeds for the agent and the API. |
| Smoke test: /health | The API returns 200 on the public URL. |
| Smoke test: /api/health/agent | Returns 200, proving the API reaches the private agent service. |
| Smoke test: /api/health/intake | Returns 200: Blob is reachable and the mailbox is reachable, or intake is disabled by config. |
| Intake check (manual) | Upload a synthetic file in the portal and email one to the test mailbox. Both appear on the case as Clean, or the email appears in the triage queue. |
| Playwright (dev only) | The six-course demo flow passes against the dev URL. |
| Approval and demo deploy | Approver is recorded; the demo environment runs the same image tag as dev. |
| Manual check | Open the demo URL, run the six-course scenario and confirm status, evidence and audit views. |

**Application URL:** read it with az containerapp show -n ca-api-demo -g rg-transfercredit --query properties.configuration.ingress.fqdn -o tsv. The agent service has no public URL by design.

## 13. Troubleshooting

| Symptom | What to check |
| --- | --- |
| Project or solution not found | Correct solutionPath, Dockerfile paths and buildContext relative to the repository root. |
| No test projects found | Correct testProjects to match your test .csproj names and folders. |
| SDK or framework mismatch | Confirm the projects target net8.0 and that UseDotNet@2 installs it. |
| npm ci fails | Commit package-lock.json; check nodeVersion matches the Dockerfile. |
| pytest cannot import app | Run from the repository root and check the package layout or set PYTHONPATH=agent. |
| Docker@2 push denied | Verify sc-acr, its permissions on the registry, and the repository name. |
| Container app cannot pull the image | Check the AcrPull role assignment and that az containerapp registry set used the managed identity. |
| AzureCLI@2 authorisation failure | Verify the connection name, subscription scope and that the pipeline is authorised to use it. |
| Smoke test: /health fails | Open the container app log stream; check ASPNETCORE_URLS, the target port (8080) and startup errors. |
| Smoke test: /api/health/agent fails | Check AGENT_BASE_URL, that the agent app uses internal ingress in the same environment, and the agent target port (8000). |
| Smoke test: /api/health/intake fails | Check the Storage Blob Data Contributor role, STORAGE_ACCOUNT_URL and the container names; if the mailbox is the cause, check the Exchange RBAC assignment or set INTAKE_ENABLED=false. |
| Graph returns 403 or access denied | Run Test-ServicePrincipalAuthorization; confirm the management scope filter matches the mailbox address and that the role is Application Mail.Read. |
| App can read other mailboxes | A tenant-wide Mail.Read consent exists in Entra. Remove it and rely on the Exchange RBAC assignment only. |
| Files stay in Pending scan status | Check that Defender for Storage on-upload malware scanning is enabled for the storage account and not excluded by a path or size filter. |
| Same email processed twice | Check that internetMessageId is unique in IntakeMessage and the delta link is saved in MailSyncState. |
| Secrets not found at runtime | Check Key Vault role assignments, the secret names and that the keyvaultref URL is correct. |
| Database connection failure | Check the connection secret, SQL firewall rules and identity permissions. |
| Demo stage never starts | The approval is waiting. Open the pipeline run and approve it, or check that the approver has access. |

## 14. Recommended production controls

- Keep dev and demo (or production) as separate environments with their own container apps and Key Vault secrets.
- Protect the demo environment with approvals and checks; add branch-control checks so only main can deploy.
- Require pull-request reviews and build validation on main.
- Use Container Apps revisions and traffic splitting for lower-risk releases and quick rollback to the previous image tag.
- Run smoke tests after every deploy and watch Application Insights for errors and slow agent calls.
- Keep the agent service on internal ingress and limit who can change its settings.
- Keep the intake mailbox scope to one mailbox, review the Exchange RBAC assignment regularly, and never grant Mail.Send or tenant-wide mail permissions.
- Keep blob public access disabled and review role assignments on the storage account.
- Pin base image and package versions, and scan images for vulnerabilities before promotion.
- Review .NET, Node and Python support dates and plan upgrades before support ends.

## 15. Official documentation

- Azure Container Apps: https://learn.microsoft.com/en-us/azure/container-apps/
- Build and push Docker images with Azure Pipelines: https://learn.microsoft.com/en-us/azure/devops/pipelines/ecosystems/containers/build-image
- Build, test and deploy .NET projects with Azure Pipelines: https://learn.microsoft.com/en-us/azure/devops/pipelines/ecosystems/dotnet-core
- Azure Pipelines environments, approvals and checks: https://learn.microsoft.com/en-us/azure/devops/pipelines/process/approvals
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- LangGraph documentation: https://langchain-ai.github.io/langgraph/
- Graph messages delta query: https://learn.microsoft.com/en-us/graph/delta-query-messages
- Graph list attachments: https://learn.microsoft.com/en-us/graph/api/message-list-attachments
- Graph get attachment: https://learn.microsoft.com/en-us/graph/api/attachment-get
- Graph change notifications: https://learn.microsoft.com/en-us/graph/webhooks
- Graph limit app mailbox access: https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access
- Exchange Online RBAC for Applications: https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac
- Blob Storage with .NET: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-quickstart-blobs-dotnet
- Authorise blob access with Entra ID: https://learn.microsoft.com/en-us/azure/storage/blobs/authorize-access-azure-active-directory
- User delegation SAS: https://learn.microsoft.com/en-us/rest/api/storageservices/create-user-delegation-sas
- Blob lifecycle management: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview
- Defender for Storage on-upload malware scanning: https://learn.microsoft.com/en-us/azure/defender-for-cloud/on-upload-malware-scanning

Sample resource names, paths and versions are examples. Check them against your own repository and Azure environment before the first run.
