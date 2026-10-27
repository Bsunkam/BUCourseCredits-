# Transfer Credit Review Assistant

Hackathon prototype for Boston University transfer-credit evaluation, built from
[Designs/BU_Transfer_Credit_AI_Roadmap_Rulebook_v2.md](Designs/BU_Transfer_Credit_AI_Roadmap_Rulebook_v2.md) and
[Designs/Azure_DevOps_CICD_Deployment_Guide_v2.md](Designs/Azure_DevOps_CICD_Deployment_Guide_v2.md).

AI suggestions are advisory only. The academic department makes every final decision. All data is synthetic, and
posting to MyBU Student is simulated.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/ui` | React + Vite + TypeScript front end. Talks only to the .NET API. |
| `src/api/TransferCredit.Api` | ASP.NET Core 8 Minimal API: cases, documents, intake, rules engine, matching, evaluations, audit. Serves the built UI from `wwwroot`. |
| `src/api/TransferCredit.Api.Tests` | xUnit unit tests (rules, reuse key, file checks) and in-memory integration tests of the whole workflow. |
| `src/database/TransferCredit.Data` | EF Core entities, `DbContext`, migrations and seed data (`Seed/*.json`). |
| `src/database/scripts/schema.sql` | Idempotent SQL script generated from the migrations. |
| `src/agent` | Python 3.11 FastAPI + LangGraph agent service (internal ingress only). |
| `contracts/agent-contract.schema.json` | Shared API ↔ agent JSON contract. |
| `docker/api.Dockerfile`, `src/agent/Dockerfile` | Container images. |
| `azure-pipelines.yml`, `pipelines/deploy.yml` | Five-stage Azure Pipelines (build/test → package → dev → approval → demo). |
| `infra/setup-azure.ps1` | One-time Azure setup: ACR and App Service apps (API and agent for dev and demo); stores each app's deployment webhook in the Azure DevOps variable group `transfercredit-deploy`. Needs only Contributor on the resource group. |
| `tests/e2e` | Playwright test of the six-course demo flow. |

## Run locally

Prerequisites: .NET 8 SDK, Node 22, SQL Server LocalDB (or any SQL Server). Python 3.11 is only needed for the
agent service.

Quick start: `.\run-local.ps1` starts the API on LocalDB and the UI in two windows; add `-WithAgent` to also set up
and start the Python agent. The manual steps follow. Behind a TLS-inspecting proxy, add `--use-feature=truststore` to
`pip install`.

```powershell
# 1. API (creates and seeds the TransferCredit database on first start)
cd src/api/TransferCredit.Api
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5090     # Swagger: http://localhost:5090/swagger

# 2. UI (proxies /api to :5090)
cd src/ui
npm install
npm run dev                                                      # http://localhost:5190
```

Pick a demo user in the top-right corner. Each role sees a different view: staff, department evaluator, admin,
advisor, or student.

With no `AGENT_BASE_URL` set, the API uses a **stub agent**: a keyword-overlap ranker labelled "not AI". To use the
real agent service:

```bash
cd src/agent
python -m venv .venv && . .venv/bin/activate       # Windows: .venv\Scripts\activate
pip install -r requirements.txt -r requirements-dev.txt
uvicorn app.main:app --port 8000                    # AGENT_MODE=mock unless AZURE_OPENAI_ENDPOINT is set
# then start the API with AGENT_BASE_URL=http://localhost:8000
```

### Tests

```powershell
dotnet test TransferCredit.sln              # 30 xUnit tests
cd src/ui; npm run lint; npm test           # typecheck + vitest
cd src/agent; pytest                        # agent graph in mock mode
cd tests/e2e; npm install; npx playwright install chromium; npx playwright test   # needs API + UI running
```

### Database

```powershell
dotnet tool restore
cd src/database/TransferCredit.Data
dotnet ef migrations add <Name> -o Migrations                       # after changing entities
dotnet ef migrations script --idempotent -o ../scripts/schema.sql   # regenerate the SQL script
```

The API applies migrations on start (`Database:AutoMigrate`), seeds reference data, and creates demo case TC-1042
when the database is empty (`Demo:SeedDemoCase`).

**Seed data is synthetic.** `Seed/catalogue.json` has the same shape as `BU_Transfer_Program_Course_Match.xlsx`
(programs, BU courses with Yes/Check verification, BU-only rules for CS 320/332/350, equivalencies). Replace its rows
with the sanitised workbook data before the sponsor demo.

## How the design maps to the code

| Design item | Where |
| --- | --- |
| Rules BR-03 (quarter ⅔), BR-04 (grade), BR-06 (deadline) | `src/api/.../Rules/RulesEngine.cs`, values in the `RuleSettings` table |
| BR-10 BU-only courses | `ProgramRules` table; enforced in matching and again on finalise (`EvaluationService`) |
| BR-01 human decision, BR-09 outcomes | `EvaluationService`: only evaluators finalise; staff can only confirm an approved equivalency |
| BR-07/BR-14 reuse and cost control (§9.3) | `MatchingService`: grade gate → approved equivalency → missing syllabus → cached run → agent. Finalising writes an `EquivalencyRule` keyed by `CacheKey` |
| BR-15 intake (§8.2) | `Documents/DocumentIntakeService` (validate, SHA-256 de-duplication, quarantine), `ScanWorker`, `Intake/EmailIntake.cs`, `Intake/GraphMailPoller.cs` |
| Agent pipeline (§9) | `src/agent/graph/pipeline.py`: intake → policy gate → matching → evidence → human_review (interrupt) |
| Audit and student-safe timeline | `AuditEvents` table; `StudentMessage` is the only text a student sees |
| AI usage evidence | `AgentRuns` table; UI **Metrics & AI evidence** page |

## Configuration

Environment variable names match the deployment guide.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings__Default` | Azure SQL (Key Vault secret `sql-conn`). Required. |
| `AGENT_BASE_URL` | Internal URL of the agent container app. Empty = stub agent. |
| `STORAGE_ACCOUNT_URL` | Blob Storage account. Empty = local disk under `App_Data/storage`. |
| `Storage__ScanMode` | `Defender` (read Defender for Storage index tags) or `Simulated` (EICAR check, local only). |
| `INTAKE_ENABLED`, `INTAKE_MAILBOX`, `INTAKE_POLL_SECONDS` | Graph mailbox polling. Off by default; use the `.eml` simulator on the Email triage page. |
| `MAX_UPLOAD_MB`, `ALLOWED_FILE_TYPES` | Upload limits (default 20 MB; pdf, docx, png, jpg). |
| Agent: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_CHAT_DEPLOYMENT`, `AZURE_SEARCH_ENDPOINT`, `AZURE_SEARCH_INDEX`, `AZURE_DOCINTEL_ENDPOINT` | Azure AI services. Without an endpoint the agent runs in mock mode. |

## Known gaps and decisions

- **Demo sign-in only.** The `X-Demo-User` header picks a seeded synthetic user. Replace it with Microsoft Entra ID
  before any real use.
- **LangGraph checkpointer is in memory.** Paused review threads are lost on restart or across replicas. The
  decision itself is always stored in SQL, and `/agent/resume` tolerates a missing thread. Use a persistent
  checkpointer if the agent runs with more than one replica.
- **AI Search index is not created by this repo.** The agent falls back to the catalogue the API sends. Build a
  `bu-courses` index with a `contentVector` field and an integrated vectoriser to enable vector retrieval.
- **Transcript field extraction** returns text only; staff confirm courses in the "Add course" form.
- **Extraction cache:** the API (Storage Blob Data Contributor) writes `extracted/{sha256}.json`; the agent only reads blobs.
- **Rounding** defaults to truncation (4 quarter credits → 2.66, as in the brief). Discovery question 8 may change it
  (`CreditRounding` = `Round` → 2.67).
- **.NET 8 support ends on 10 November 2026.** Plan the move to .NET 10 (LTS). Node 22 is used instead of Node 20,
  which is already out of support.
- The design's `images/*.png` diagrams are not in the repository.
