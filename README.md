# Transfer Credit Review Assistant

Hackathon prototype for Boston University transfer-credit evaluation, built from
[Designs/BU_Transfer_Credit_AI_Roadmap_Rulebook_v2.md](Designs/BU_Transfer_Credit_AI_Roadmap_Rulebook_v2.md) and
[Designs/Azure_DevOps_CICD_Deployment_Guide_v2.md](Designs/Azure_DevOps_CICD_Deployment_Guide_v2.md).

AI suggestions are advisory only. The academic department makes every final decision. All data is synthetic, and
posting to MyBU Student is simulated.

The hackathon write-up (problem breakdown, the one measurable outcome, assumptions, discovery questions and the
evaluation result) is in [docs/TeamResponse.md](docs/TeamResponse.md).

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

**Real AI locally:** copy `src/agent/.env.example` to `src/agent/.env` and fill in the Azure OpenAI endpoint, key and
deployment (gpt-4o). `.\run-local.ps1` then starts the agent automatically in Azure mode, so suggestions come from the
model. `.env` is git- and docker-ignored. Behind a TLS-inspecting proxy, also set `SSL_CERT_FILE` there to a PEM bundle
that includes the proxy's root certificate.

When the API uses the agent service, it never reuses a keyword ranking ("… (not AI)") as an AI suggestion: courses
matched earlier in mock or stub mode are matched again by the model.

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

Sign in with one of the seeded accounts. In Development every account's password is `TransferDemo#2026`
(`Auth:SeedPassword` in `appsettings.Development.json`), and the sign-in page lists the usernames:

| Username | Role |
| --- | --- |
| `staff-1` | Credit Evaluation staff |
| `eval-cs` | Department evaluator (CS) |
| `admin-1` | Admin (also manages accounts on **Users & roles**) |
| `advisor-1` | Advisor (read-only) |
| `student-a`, `student-b` | Students DS-0001 and DS-0002 |

You can also sign in with the account's email address. Each role sees a different view.

### Setting real usernames, emails and passwords for existing accounts

1. Write a CSV file **outside the repository**, for example `D:\Source\users-import.csv`. Files named
   `*users-import*.csv` are git-ignored as a safety net.

   ```csv
   currentUsername,newUsername,email,password
   staff-1,s.rivera,s.rivera@bu.edu,<password>
   eval-cs,,e.chen@bu.edu,"<password, with a comma>"
   ```

   - `currentUsername` is the existing account: `staff-1`, `eval-cs`, `admin-1`, `advisor-1`, `student-a` or `student-b`.
   - Leave `newUsername` or `email` empty to keep the current value.
   - Roles and student references never change.
   - Put a password in double quotes if it contains a comma or a quote.
2. Stop the API: the command shares its build output. Then run it from `src/api/TransferCredit.Api`, against the
   database the connection string points at:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   dotnet run --no-launch-profile -- import-users D:\Source\users-import.csv --dry-run   # check only
   dotnet run --no-launch-profile -- import-users D:\Source\users-import.csv            # apply
   ```

   - Every row is checked first. If any row is invalid, no account changes, and the command lists each problem
     without showing passwords.
   - Imported accounts must choose a new password at first sign-in. Add `--no-password-change` if each person chose
     their own password.
   - Open sessions end, and lockouts are cleared.
   - Each change is audited as `UserImported`. Past audit events keep the old username.
3. Delete the CSV file.

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
dotnet test TransferCredit.sln              # 48 xUnit tests (12 cover sign-in and roles)
cd src/ui; npm run lint; npm test           # typecheck + vitest
cd src/agent; pytest                        # agent graph in mock mode (12 tests)
cd src/agent; $env:AGENT_MODE="mock"; python -m eval.run_eval eval/labelled_cases.json   # match agreement table
cd tests/e2e; npm install; npx playwright install chromium; npx playwright test   # needs API + UI running
```

### Database

```powershell
dotnet tool restore
cd src/database/TransferCredit.Data
dotnet ef migrations add <Name> -o Migrations                       # after changing entities
dotnet ef migrations script --idempotent -o ../scripts/schema.sql   # regenerate the SQL script
```

The API applies migrations on start (`Database:AutoMigrate`), seeds reference data, and creates a demo case when the
database is empty (`Demo:SeedDemoCase`). Seeding is additive: on every start, catalogue rows missing from the database
are added, so an existing database picks up new courses and past decisions.

**Seed data is synthetic** and sized like the brief's sample data. `Seed/catalogue.json` is generated by
`src/database/scripts/generate_synthetic_data.py` (deterministic). It holds:

- 155 BU courses for the CS program and its supporting departments, with learning outcomes and Yes/Check verification
- 214 public-catalogue courses from Northeastern and NYU, plus a synthetic community college and quarter-system college
- 104 past decisions (approved equivalencies)
- the BU-only rules for CS 320/332/350

The same script writes the 93 labelled evaluation cases in `src/agent/eval/labelled_cases.json`. Replace the data with
the organisers' files when supplied.

**Demo cases** (`Seed/demo.json`, buttons on the Cases page):

- *New six-course demo case* is the brief's scenario: a community-college student with 3 existing equivalencies, 2
  "maybe" courses and 1 quarter-system course.
- *Northeastern demo case* shows the BU-only block, the grade gate and a mapping held for confirmation.

## How the design maps to the code

| Design item | Where |
| --- | --- |
| Rules BR-03 (quarter ⅔), BR-04 (grade), BR-06 (deadline) | `src/api/.../Rules/RulesEngine.cs`, values in the `RuleSettings` table |
| BR-10 BU-only courses | `ProgramRules` table; enforced in matching and again on finalise (`EvaluationService`) |
| Step 4: existing equivalency applied | `MatchingService.ApplyApprovedRuleAsync`: recorded as a final decision by `system:equivalency-db`. Held for staff confirmation when the grade can't be checked or the mapping is marked Check |
| Step 7: grade T; BR-05 no Hub units; external grades not in BU GPA | `RulesEngine.TransferGrade` / `HubUnits`; posted record in the student view; `CaseService.PostSimulatedAsync` |
| "How much of my degree progress is secure" | `CaseService.GetStudentStatusAsync`: secured, pending and at-risk units, secured units by requirement |
| Primary outcome (time at zero units) | `GET /api/metrics` (`medianHoursAtZeroUnits`); Metrics page |
| BR-01 human decision, BR-09 outcomes | `EvaluationService`: only evaluators finalise; staff can only confirm an approved equivalency as it stands (same outcome, course and units); elective credit must award more than 0 units. A decision that depended on the student's grade is not saved for reuse |
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
| `Auth__SeedPassword`, `Auth__SeedPasswordMustChange` | Password for seeded accounts that have none. Empty (default) = they cannot sign in. |
| `Auth__MaxFailedSignIns`, `Auth__LockoutMinutes`, `Auth__SessionHours`, `Auth__SignInAttemptsPerMinute`, `Auth__MinPasswordLength` | Sign-in limits (defaults 5, 15, 8, 10, 10). |
| `AGENT_BASE_URL` | Internal URL of the agent container app. Empty = stub agent. |
| `STORAGE_ACCOUNT_URL` | Blob Storage account. Empty = local disk under `App_Data/storage`. |
| `Storage__ScanMode` | `Defender` (read Defender for Storage index tags) or `Simulated` (EICAR check, local only). |
| `INTAKE_ENABLED`, `INTAKE_MAILBOX`, `INTAKE_POLL_SECONDS` | Graph mailbox polling. Off by default; use the `.eml` simulator on the Email triage page. |
| `MAX_UPLOAD_MB`, `ALLOWED_FILE_TYPES` | Upload limits (default 20 MB; pdf, docx, png, jpg). |
| Agent: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_CHAT_DEPLOYMENT`, `AZURE_SEARCH_ENDPOINT`, `AZURE_SEARCH_INDEX`, `AZURE_DOCINTEL_ENDPOINT` | Azure AI services. Without an endpoint the agent runs in mock mode. |

## Known gaps and decisions

- **Local accounts, not single sign-on.** Accounts live in the `Users` table and sign in with a username and password:
  - Passwords are hashed with PBKDF2 (ASP.NET Core Identity `PasswordHasher`).
  - The session is an HttpOnly, SameSite=Strict cookie, Secure in Production.
  - Every request is checked against the account, so disabling an account, changing its role or resetting its password
    ends its sessions at once.
  - Five wrong passwords lock the account for 15 minutes.
  - Sign-in is rate limited per client IP.
  - Admins create accounts, change roles, reset passwords and unlock accounts on **Users & roles**. New and reset
    accounts must choose their own password at the next sign-in.

  For BU use, add Microsoft Entra ID single sign-on and map its app roles to `UserRole`; the policies stay as they are.
- **Bootstrapping an environment:** seeded accounts get no password unless `Auth__SeedPassword` is set. In Azure, set
  it from Key Vault for the first start, sign in as `admin-1`, choose a new password, set the other accounts' passwords,
  then remove the setting. `Auth__SeedPasswordMustChange` defaults to true outside Development.
- **Behind a proxy**, the sign-in rate limit sees the proxy's address unless forwarded headers are configured. The
  per-account lockout does not depend on the address. With more than one API instance, share the Data Protection keys
  (App Service does this by default) so sessions survive moving between instances.
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
