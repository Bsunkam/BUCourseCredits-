# SMARTER TRANSFER CREDIT EVALUATION

**Project Roadmap • Rulebook • Tech Stack • Process Flows**

Prepared from the BU problem statement, the transfer program/course match workbook, and the Hackathon Discovery Log. **Updated stack:** React front end, ASP.NET Core 8 API, and a Python (FastAPI + LangGraph) agent service, hosted on Azure.

**Purpose:** a practical plan to build a hackathon prototype that speeds up transfer-credit review, makes case status visible, and keeps academic departments responsible for the final decision.

## 1. Executive summary

The proposed solution is a reviewer-assist application for transfer-credit evaluation. It takes in course details and syllabus evidence, checks known equivalencies and explicit academic rules, and uses a **multi-agent pipeline** to rank possible BU course matches and explain why each was suggested. Uncertain cases go to a department evaluator. The application also tracks the case lifecycle and records the final human decision.

- **Primary outcome:** reduce manual effort and improve visibility for pending transfer-credit cases.
- **Non-negotiable boundary:** AI suggestions are advisory only. The academic department makes the final credit decision.
- **Prototype approach:** use the course-match workbook as seed and reference data. Use only supplied hackathon data, or clearly labelled synthetic data for missing transcripts and syllabi.
- **Architecture in one line:** React talks only to the .NET API. The .NET API owns data, rules and audit, and calls a private Python agent service for the AI work.
- **Two intake channels:** students can upload transcripts and syllabi in the portal or email them to an intake mailbox. Both go through one pipeline: validate, scan, store in Blob Storage and match to a case (section 8.2).
- **Cost control:** approved evaluator decisions are saved and reused, so a course BU has already seen costs no further AI calls (section 9.3).
- **No live integration:** do not connect to MyBU Student or post academic records unless the sponsor explicitly authorises a safe integration. Final posting is simulated.

## 2. Level 1 qualification checklist coverage

The kickoff checklist lists seven must-haves, and a team is eliminated for having no working prototype, no AI usage evidence, a missed deadline, a major deviation from the problem statement, or non-compliant behaviour. This table shows where each item is covered.

| Must-have | How this plan covers it | Evidence to show judges |
| --- | --- | --- |
| Discovery | Eight prioritised sponsor questions (section 17), logged in the Discovery Log, with answers applied to the rules and demo. | Question log, answers received, and a list of what changed because of them. |
| Agent pipeline | LangGraph graph with separate agents for intake, matching, policy gate and evidence, plus a human review step. | Graph diagram, agent roles table, run traces (LangSmith if available). |
| AI services | Azure AI Document Intelligence, Azure OpenAI and Azure AI Search used inside the solution itself. | Stored agent runs showing model, prompt version, inputs and outputs. |
| Live app demo | Both services deployed to Azure Container Apps through Azure Pipelines. | A public URL and a successful pipeline run. |
| UI | React app with a student status timeline, upload page and evaluator workbench. | Live walkthrough of the six-course scenario. |
| Backend | ASP.NET Core API with Azure SQL, Blob Storage, a rules engine and an audit log. | Working endpoints, seeded data, audit records. |
| Testing | xUnit, pytest, Playwright and an evaluation run against past decisions. | Test results from the pipeline and an accuracy table. |

## 3. What the uploaded files tell us

| Source | Source-derived information | Implication for build |
| --- | --- | --- |
| Boston University.docx | Problem statement: 800+ transfer students a year, repeated manual syllabus comparison, status uncertainty, pending zero units, and a one-year completion rule. | Build case tracking, course-level matching, evidence review and deadline visibility. |
| Problem workflow in brief | Existing equivalencies are applied when found. Otherwise the course becomes "maybe" and needs syllabus review. The evaluator can award a BU equivalent, elective units, or no credit. | Implement these states and decision outcomes explicitly. |
| BU_Transfer_Program_Course_Match.xlsx | Program rules for five areas (Computer Science, Economics, Psychology, Biology, Mathematics); BU, Northeastern and NYU course sheets; match levels; lookup and summary sheets. | Use as the initial reference catalogue and demo lookup data. Keep the verified/check flags and source links. |
| Hackathon Discovery Log.xlsx | A question log template with one example row about monthly volume and turnaround, using placeholders such as [Participant name]. | Treat the example row as a template, not as business evidence. Replace it with sponsor responses. |

> **Data gap.** The brief mentions sample transcripts, course descriptions and past decisions, and says syllabi and the equivalency extract still need confirmation. The uploaded files do not include a separate transcript or syllabus bundle or a clean past-decision export. Confirm what is available. Until then, use synthetic cases for upload and matching demonstrations.

## 4. Problem statement and product goal

**Problem:** students and reviewers carry avoidable effort because course equivalencies and syllabus evidence are compared by hand, while pending status is not clearly visible.

**Proposed product:** Transfer Credit Review Assistant, a secure workflow application that combines a rules engine, catalogue and equivalency lookup, an agent pipeline for explainable candidate ranking, an evaluator workbench and a case-status timeline.

| User | Needs | Prototype capability |
| --- | --- | --- |
| Credit Evaluation staff | Process cases, identify known matches, see missing data and deadlines. | Case queue, bulk course list, rules-based checks, case status, and a triage queue for emails that cannot be matched to a case. |
| Department evaluator | Compare course content and evidence; make defensible decisions. | Candidate comparison, syllabus viewer, reason and evidence panel, approve/edit/reject. |
| Student-facing status (demo only) | Understand what is pending and what action is needed. | Status timeline, portal document upload, missing-syllabus request, pending/final state; no private reviewer notes. |
| Advisor / support role | Understand progress without deciding equivalency. | Read-only case summary and progress indicators. |

## 5. Success metrics and validation plan

The brief gives no verified baseline for handling time or error rate. Establish these with the sponsor before claiming improvement. The Discovery Log example (45 requests a month, 18 days) is a sample row and must not be treated as confirmed fact.

| Metric | How to measure in prototype | Baseline / target handling |
| --- | --- | --- |
| Time to first useful match | Time from course ingestion to candidate list shown. | Measure on the same fixed test cases; compare manual lookup against the prototype. |
| Reviewer handling time | Active reviewer minutes per course or case, excluding wait time. | Capture timestamps or run a controlled timed test. |
| Match quality | Agreement of the top suggestion with evaluator-confirmed labels. | Use a holdout of past decisions if approved; otherwise label synthetic test cases. |
| Evidence completeness | Share of cases with transcript fields and required syllabus evidence attached. | Calculate from the case checklist. |
| Status transparency | Share of cases with visible state, next action and owner. | Acceptance check using the sample student view. |
| Human-control compliance | Share of final decisions with a named authorised reviewer and audit record. | Target 100% for prototype acceptance. |
| Deadline visibility | Share of cases showing start date and one-year deadline. | Target 100% when dates are available. |
| Reuse rate | Share of courses resolved from an approved equivalency or cached suggestion without an AI call. | Report per run. Shows the cost saving; the second-student demo makes it visible. |
| AI cost per case | Model, extraction and search calls per case, from stored agent runs. | Compare first-time cases with repeat cases. |
| AI usage evidence | Share of agent runs stored with model, prompt version, inputs and outputs. | Target 100%. Required for qualification. |

## 6. Recommended technology stack

The stack is a hybrid. The .NET team builds the API and the React UI. A Python service runs the LangGraph agents because LangChain and LangGraph are Python-first.

| Layer | Recommended choice | Why / notes |
| --- | --- | --- |
| Frontend | React single-page app (Vite), served from the .NET API's static files. | One origin, no CORS. Talks only to the .NET API, never to the agent service. |
| Backend API | ASP.NET Core 8 (Minimal APIs), EF Core, FluentValidation or built-in validation. | Owns cases, documents, intake, decisions, audit and status. Single front door for the UI. Hosts the email intake worker. |
| Business rules | C# policy classes in the API. Rule values (grade thresholds, conversion factor) stored in SQL where useful. | Deterministic academic rules are never left to a model. The API computes the flags and passes them to the agents. |
| Agent service | Python 3.11, FastAPI, Pydantic models matching the shared JSON contract. | Runs the graph and returns structured results. No UI and no database logic. |
| Agent orchestration | LangGraph (graph, conditional routing, human-in-the-loop interrupt) with LangChain for model integration. | Provides agents across roles, not just code assist. LangSmith tracing if the org provides it. |
| AI services | Azure AI Document Intelligence (PDF/OCR), Azure OpenAI (embeddings and summaries), Azure AI Search (vector index of BU courses). | Model output is candidate evidence only. Never a final credit decision. Subject to sponsor approval of data sent. |
| Database | Azure SQL (serverless), EF Core migrations. | Catalogue, cases, candidates, decisions, rules, agent runs (with cache keys) and audit trail. |
| File storage | Azure Blob Storage with three private containers (intake-raw, documents, extracted). | Files in Blob, metadata in SQL. No public access; managed identity instead of account keys. |
| Portal upload | React upload panel and POST /api/cases/{id}/documents. | The MVP route for students and staff to add transcripts and syllabi. |
| Email intake | Microsoft Graph (Mail.Read, app-only) read by a background worker in the .NET API. | Handles documents students send by email. Scoped to one intake mailbox. |
| Malware scanning | Microsoft Defender for Storage on-upload scanning. | Every file, from either channel, is scanned before it is used. |
| Identity | ASP.NET Core Identity or Microsoft Entra ID if available; role-based authorisation. | Separate staff, evaluator, admin and student/demo views. |
| Secrets | Azure Key Vault with managed identity. | No keys in the repo, YAML or app settings files. |
| Hosting | Azure Container Apps: one app for the .NET API (public), one for the agent service (internal ingress only). Images in Azure Container Registry. | Same environment so the API can call the agent service on an internal address. |
| CI/CD | Azure Repos + Azure Pipelines (YAML, multi-stage). | Build, test, package images, deploy, smoke test. See the companion CI/CD guide. |
| Observability | ILogger and Application Insights; structured audit events; LangSmith traces. | Traceability of decisions and agent behaviour. |
| Testing | xUnit, pytest, Playwright, plus an evaluation script over past decisions. | Rules, agents, end-to-end flow and match accuracy. |

> **Architecture principle.** Keep the first version modular but not over-engineered: one API, one agent service. Add no further services unless a confirmed requirement needs them.

## 7. Logical architecture diagram

![arch](images/arch.png)

*Figure 1. Logical architecture: portal and email intake, ASP.NET Core API, private Python agent service, Azure data and AI services.*

The agent service is optional at runtime. If it is down or times out, the .NET API must still support exact equivalency lookup, rule validation, workflow and the evaluator decision. The failure is shown in the UI and logged.

## 8. Core process flow and document intake

### 8.1 Core process flow

![flow](images/flow.png)

*Figure 2. Transfer credit flow from received documents to simulated posting.*

> **Prototype note.** The posting step is a simulated outcome and status update unless the sponsor provides approved integration access. The brief says final outcomes are posted in MyBU Student but does not authorise credentials or an API.

### 8.2 Document intake: portal upload and email

Students may send transcripts, course details and syllabi through the portal or by email to BU. Both channels feed one intake pipeline, so every file is validated, scanned, stored and matched to a case in the same way.

> **Is portal upload already included?** The earlier plan had an upload page and an upload endpoint, but only as "upload or demo attach". It is now a firm MVP feature with the design below. Email intake is new. How students really send documents today is discovery question 2, so the answer may change the priority of each channel.

| Channel | How it works | Prototype scope |
| --- | --- | --- |
| Portal upload | The student or staff member opens a case and uploads PDF, Word or image files. React sends a multipart request to POST /api/cases/{id}/documents. The API validates the file and streams it to Blob Storage. | MVP, Phase 3. |
| Email | The student emails the intake mailbox with files attached. A background worker in the API finds new messages through Microsoft Graph, downloads the file attachments and runs the same pipeline. | MVP with a test mailbox and synthetic emails, Phase 4. |
| Staff upload on behalf | Credit Evaluation staff attach a file that arrived another way (for example a scan). Same endpoint, recorded as Source = StaffUpload. | MVP, Phase 3. |

![intake](images/intake.png)

*Figure 3. Document intake pipeline for portal upload and email.*

### Intake pipeline steps

1. **Receive:** a portal request arrives, or the worker finds a new email through Graph.
2. **Validate:** allow only PDF, DOCX, PNG and JPG; enforce a size limit (for example 20 MB); reject empty files; check that the file signature matches the extension.
3. **De-duplicate:** compute a SHA-256 checksum. If the same file already exists, link it to the case without storing or extracting it again (this supports the cost control in section 9.3). For email, also skip messages already seen, using the internetMessageId.
4. **Store:** write the file to the private intake-raw container (quarantine).
5. **Scan:** Defender for Storage scans on upload. Only files with a clean result are copied to the documents container. Infected files stay quarantined and are flagged in the staff queue.
6. **Match to a case:** use a case reference in the subject (for example TC-1042) or a sender address that matches the case contact. Exactly one match attaches the file. None or several send it to the triage queue, where staff choose the case.
7. **Extract:** clean documents go to text extraction through the agent service. Results are cached by checksum.
8. **Show status:** the case timeline records "Document received" with the channel and time. No automatic reply email is sent in the MVP.

### Email through Microsoft Graph

| Item | Choice | Why |
| --- | --- | --- |
| Mailbox | A dedicated intake mailbox (for example transfer-intake@yourdomain). For the hackathon use a test mailbox with synthetic emails only. | Keeps real student mail out of the prototype and limits access to one mailbox. |
| Permission | Application permission Mail.Read, granted through Exchange Online RBAC for Applications and scoped to the intake mailbox only. | Mail.Read is the least privileged permission for listing messages and reading attachments. No Mail.ReadWrite or Mail.Send is needed. |
| Scoping | Do not grant tenant-wide Mail.Read in Entra. Grant the Exchange RBAC role with a management scope instead. | A tenant-wide Graph permission bypasses the Exchange scope, so the app would see every mailbox. |
| Identity | Managed identity of the API container app (preferred), or an Entra app registration with its secret in Key Vault. | No secrets in code or YAML. |
| Detecting new mail | Start with a worker that polls the inbox with a delta query every 1 to 2 minutes and stores the delta link in SQL. Move to change-notification webhooks later. | Polling needs no public endpoint and no renewals. Outlook message subscriptions last under 7 days (under 1 day with resource data) and must be renewed. |
| Processed tracking | Store each message ID and internetMessageId in SQL. Do not move or mark messages. | Avoids needing write access to the mailbox. |
| Attachment types | Process file attachments only. Ignore inline images. Reference attachments (cloud links) cannot be downloaded this way and go to triage. Forwarded-email attachments go to triage. | Matches how Graph treats each attachment type. |
| Email body | Used only to find a case reference. Never sent to AI. Treated as untrusted. | Protects against prompt injection and limits personal data. |
| Replies | None in the MVP. Mail.Send is not requested. | Sending mail is a later feature that needs explicit approval. |

| Purpose | Graph request (base URL https://graph.microsoft.com/v1.0) |
| --- | --- |
| Find new messages | GET /users/{mailbox}/mailFolders/inbox/messages/delta |
| List attachments | GET /users/{mailbox}/messages/{id}/attachments |
| Download a file attachment | GET /users/{mailbox}/messages/{id}/attachments/{attachmentId}/$value |

Delta queries work per folder, so only the inbox is tracked. Use the Microsoft Graph .NET SDK from the intake worker, with the managed identity as the credential.

### Blob Storage layout

| Container | Contents | Access |
| --- | --- | --- |
| intake-raw | Every file as received, before scanning. | Written by the API identity only. Never read by users or AI. |
| documents | Clean files, at {caseId}/{documentId}/{sha256}.{ext}. | Read through the API. Evaluators get short-lived user delegation SAS links. |
| extracted | JSON text from Document Intelligence, keyed by checksum. | Read by the agent service. Supports reuse. |

- Blob URLs look like https://<storage-account>.blob.core.windows.net/<container>/<path>. With public access disabled they work only for an identity with a role, or with a SAS token.
- The API uses its managed identity with the Storage Blob Data Contributor role. No account keys.
- Metadata (file name, type, size, checksum, scan status, source) lives in SQL. The file bytes stay in Blob.
- Add a lifecycle rule to delete intake-raw files after a retention period. The period is a sponsor item.

Minimal upload code in the API:

```
var service = new BlobServiceClient(
    new Uri(config["STORAGE_ACCOUNT_URL"]), new DefaultAzureCredential());
var blob = service.GetBlobContainerClient("intake-raw")
                  .GetBlobClient($"{caseId}/{documentId}/{sha256}{ext}");
await blob.UploadAsync(stream, overwrite: false);
```

### Portal upload details

- **Screen:** an upload panel on the case page with drag and drop. Each file shows a status: Received, Scanning, Clean, Quarantined or Extracted.
- **Endpoint:** POST /api/cases/{id}/documents as multipart/form-data, with documentType (Transcript, Syllabus or Other) and an optional sourceCourseId for syllabi.
- **Access:** a student can upload only to their own case. Staff and evaluators can upload to any case.
- **Limits:** same type, size and signature checks as email.
- **Later optimisation:** direct-to-Blob upload with a short-lived SAS link, so large files skip the API.

### If tenant admin consent is not available

Setting up the mailbox scope needs an Exchange administrator. Ask your IT team early. If it is not possible in time, add an **email simulator**: a staff page that imports an .eml file and sends it through the same intake pipeline. The demo then shows the full email path without a live mailbox, and you can state clearly that the Graph connection is built but not enabled.

### Official documentation for intake

| Topic | URL |
| --- | --- |
| Graph: messages delta query | https://learn.microsoft.com/en-us/graph/delta-query-messages |
| Graph: list attachments | https://learn.microsoft.com/en-us/graph/api/message-list-attachments |
| Graph: get attachment | https://learn.microsoft.com/en-us/graph/api/attachment-get |
| Graph: change notifications (webhooks) | https://learn.microsoft.com/en-us/graph/webhooks |
| Graph: subscription lifetimes | https://learn.microsoft.com/en-us/graph/api/resources/subscription |
| Graph: limit app access to mailboxes | https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access |
| Exchange Online RBAC for Applications | https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac |
| Graph: app-only authentication | https://learn.microsoft.com/en-us/graph/auth-v2-service |
| Graph: permissions reference | https://learn.microsoft.com/en-us/graph/permissions-reference |
| Graph SDKs | https://learn.microsoft.com/en-us/graph/sdks/sdks-overview |
| Blob Storage with .NET | https://learn.microsoft.com/en-us/azure/storage/blobs/storage-quickstart-blobs-dotnet |
| Authorise blob access with Entra ID | https://learn.microsoft.com/en-us/azure/storage/blobs/authorize-access-azure-active-directory |
| User delegation SAS | https://learn.microsoft.com/en-us/rest/api/storageservices/create-user-delegation-sas |
| Blob lifecycle management | https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview |
| Defender for Storage: on-upload malware scanning | https://learn.microsoft.com/en-us/azure/defender-for-cloud/on-upload-malware-scanning |
| Defender for Storage: malware scanning overview | https://learn.microsoft.com/en-us/azure/defender-for-cloud/defender-for-storage-malware-scan |

## 9. Agent pipeline and AI matching flow

![ai](images/ai.png)

*Figure 4. LangGraph agent pipeline with the policy gate and the human review pause.*

### 9.1 Agent roles

| Agent / node | Responsibility | Output | AI or deterministic |
| --- | --- | --- | --- |
| Intake agent | Extract course code, title, credits, grade, term and learning outcomes from transcript and syllabus text. | Structured course record plus missing-field list. | AI (Document Intelligence + model) |
| Matching agent | Retrieve top BU candidates from Azure AI Search, compare description, outcomes, level and credit hours. | Ranked candidates, match level, confidence band. | AI (embeddings + model) |
| Policy gate | Route using flags supplied by the .NET rules engine (BU-only course, grade below threshold, quarter conversion applied). | Allowed or blocked path. | Deterministic code. No calculation here. |
| Evidence agent | Write a short explanation with cited snippets for the evaluator and a safe status message for the student. | Explanation, student message, insufficient-evidence flag. | AI (model) |
| Human review | Evaluator approves, edits or rejects. The graph pauses here by design. | Final decision with rationale. | Human |

### 9.2 LangGraph design

- **State:** courseId, source course fields, syllabus text, policy flags, candidates, evidence, confidence, needsHumanReview, graph and prompt versions.
- **Routing:** exact approved equivalency skips the matching agent. A BU-only course goes straight to review with the rule shown. No syllabus means "maybe" and a request for the syllabus.
- **Human-in-the-loop:** use the graph's interrupt so nothing is finalised until an authorised evaluator responds.
- **Structured output:** each node returns a validated Pydantic model, so the .NET API receives the same shape every time.

> **Explainability requirement.** Every suggested match must show the source catalogue, candidate BU course, matched attributes, credit comparison, rule flags, confidence band and why a syllabus review is needed. Never present an AI similarity score as an official equivalency.

### 9.3 Reuse and cost control

If another student from the same university, in the same program, brings the same courses, extracting and matching them again wastes AI cost and evaluator time. The pipeline therefore checks the cheapest option first and calls AI only as a last resort.

| Order | Check | AI cost | When it applies |
| --- | --- | --- | --- |
| 1 | Approved equivalency lookup | None | Same institution and course, and an evaluator already approved a BU outcome, and the cache key matches exactly. |
| 2 | Cached suggestion | None | The same course was run before but never reviewed. Reuse the suggestion, marked "suggested, needs review". |
| 3 | Extraction cache | None | The same file (matched by checksum) was already extracted. |
| 4 | Full agent run | Yes | Nothing above matches. |

**Write-back is the key step.** When an evaluator finalises a "maybe" course, the outcome is saved as an approved equivalency (BR-07). The next student with that course resolves at check 1. AI cost is paid once per unique course, and the evaluator's work is reused.

### Cache key

Build the key from a hash of: institution and course code, course title, source credits and credit system, syllabus text hash or file checksum, target BU program, rules version and prompt version. If any part changes, the key changes and the course is evaluated again. A new prompt version therefore invalidates old suggestions automatically.

### What must not be reused

| Item | Rule |
| --- | --- |
| Per-student facts | Grade, term and credit conversion belong to the student, not the course. Recompute them with the cheap C# rules every time, so one student's B+ never carries over to another student's C-. |
| Unreviewed suggestions | A cached AI suggestion still goes to an evaluator. Only an evaluator-approved outcome counts as an equivalency. |
| Changed inputs | Reuse only when everything in the cache key is identical. If the syllabus, credits, program, rules version or prompt version differs, evaluate again. Expiry dates can be added later if the sponsor asks for them. |
| Other programs | An approval for one BU program does not apply to another. A BU-only rule (for example CS 320 in the CS program) still blocks the course. |

### Other savings

- Compute BU course embeddings once when the AI Search index is built, never per case.
- Use a smaller model for extraction and the full model only for matching and explanations.
- Skip the matching agent when policy flags already decide the path (BU-only course or missing syllabus).
- Extract once per document, not once per course, when a transcript lists many courses.

## 10. Rulebook: mandatory business rules

These rules come from the problem statement and workbook and should be confirmed with the sponsor before any production use. The .NET rules engine is the single source of truth for them.

| Rule ID | Rule | Implementation requirement |
| --- | --- | --- |
| BR-01 | Academic departments make final decisions. | Agents suggest and rank; only an authorised evaluator can finalise the outcome. |
| BR-02 | Pending credit displays as zero units until evaluated. | Use a pending status; never show pending units as awarded credit. |
| BR-03 | Quarter-system credits convert at two-thirds of a semester credit; a 4-credit quarter course becomes 2.66 BU credits. | Keep source credits, source system and converted credits separately; use decimal precision and policy-approved rounding. Calculate in C# and unit test it. |
| BR-04 | Many BU schools require a C or higher; external grades do not count toward BU GPA. | Make the minimum grade configurable by school/program; never add transfer grades to BU GPA. Confirm school-specific thresholds. |
| BR-05 | Transfer courses do not earn BU Hub general education units. | Do not assign BU Hub units to transfer-credit decisions. |
| BR-06 | All credits must be transferred within one year of starting at BU. | Track start date, deadline, days remaining and overdue status. Confirm the exact deadline policy. |
| BR-07 | An existing approved equivalency can be applied when found. | Reuse a mapping only if its source, scope, effective date and approval status are valid; flag stale or unverified records. |
| BR-08 | Unknown or no match becomes "maybe" and needs syllabus review. | Request the missing syllabus and route to an evaluator; never infer equivalency from title alone. |
| BR-09 | Evaluator outcomes: BU equivalent, elective credit that counts as units, or no credit (0 units). | Model explicit decision types with awarded units, rationale, reviewer and timestamp. |
| BR-10 | Some BU courses are not transferable under program rules; the workbook lists CS 320, CS 332 and CS 350 as BU-only for the CS program. | Support program-specific non-transferable rules and display the source rule. Validate scope and current policy. |
| BR-11 | Workbook match levels: "Likely equivalent", "Partial - syllabus review", "No equivalent found", "Must be taken at BU". | Keep these labels or map them transparently; do not collapse "partial" into approved. |
| BR-12 | Catalogue records carry a verification status: Yes means code and title confirmed; Check means details need confirmation. | Show status and source link; never present unchecked details as confirmed facts. |
| BR-13 | Use only supplied hackathon data; no real personal or student data. | Use the workbook and synthetic student/transcript records. Restrict document access and avoid real identifiers. |
| BR-14 | Reuse approved outcomes to avoid repeat work (supports BR-07). | Save each finalised evaluator outcome as an approved equivalency. For now, reuse whenever the cache key is identical (same institution, course, credits, syllabus, program, rules version and prompt version). Never reuse grade, term or conversion across students; never treat a cached AI suggestion as approved. |
| BR-15 | Documents may arrive by portal upload or by email; both are untrusted until scanned and matched. | Run every file through the same pipeline: validate, de-duplicate, store in quarantine, scan, match to a case. Send unmatched or ambiguous items to a staff triage queue; never auto-assign a doubtful match. Do not use real student mail in the prototype. |

## 11. Technical and governance rules

- **Human in the loop:** no auto-approval or auto-posting based on AI output.
- **Auditability:** every decision records case ID, course ID, reviewer ID and role, decision, rationale, evidence references, timestamp, and rule and matching versions.
- **Least privilege:** students must not see internal reviewer notes or other students' records.
- **Uploads:** validate file type, size and malware-scanning policy; store files privately; use time-limited access links if needed.
- **Privacy:** use synthetic names and IDs for the demo; send no personal student information to AI services.
- **Deterministic maths:** credit arithmetic, grade eligibility, deadline calculation and non-transferable rules live in unit-tested C# code.
- **Untrusted documents:** treat uploaded syllabus text as untrusted. Never follow instructions found inside documents; extract only course-related evidence.
- **One front door:** the agent service accepts calls only from the .NET API (internal ingress). The UI never calls it.
- **Validated contract:** reject any agent response that does not match the shared JSON schema; fall back to manual review.
- **AI traceability:** record model, provider, prompt and graph versions, candidate evidence and score for every agent run. The system must remain usable without AI.
- **No invention:** never invent a missing syllabus, credit value, course outcome, equivalency or sponsor-confirmed policy. Mark it unknown and request verification.
- **Distinct states:** show suggested / needs review / approved / rejected clearly; use no visual language that implies an AI suggestion is an official BU decision.
- **Secrets:** keep API keys and connection strings in Key Vault, never in the repository or pipeline YAML.
- **Mailbox access:** read-only (Mail.Read), scoped to the intake mailbox through Exchange RBAC for Applications. No Mail.Send and no tenant-wide mail permission.
- **Untrusted files and email:** every attachment is scanned before use; email bodies are never sent to AI; unmatched items go to a triage queue.
- **Storage access:** private containers, no public access, managed identity instead of account keys, short-lived SAS links for viewing.

## 12. Proposed data model

| Entity | Key fields (suggested) | Purpose |
| --- | --- | --- |
| TransferCase | CaseId, DemoStudentRef, Program, StartDate, DeadlineDate, Status, CreatedAt | Case lifecycle and deadline tracking. |
| SourceInstitution | InstitutionId, Name, CreditSystem, CatalogueSource, VerifiedAt | Institution and credit-system context. |
| SourceCourse | SourceCourseId, InstitutionId, Code, Title, Credits, ConvertedCredits, Grade, Term, Description, SyllabusFileId | Imported transcript and course details. |
| BUCourse | BUCourseId, Code, Title, Program, RequirementType, Credits, VerifiedStatus, SourceUrl, EffectiveDate | BU catalogue reference (also indexed in Azure AI Search). |
| EquivalencyRule | RuleId, SourceCourseId or source pattern, BUCourseId, ApprovalStatus, Scope, EffectiveFrom/To, DecisionSource, CreatedFromEvaluationId | Prior approved mappings and their validity. Created when an evaluator finalises a course. |
| MatchCandidate | CandidateId, SourceCourseId, BUCourseId, MatchLevel, Score, EvidenceJson, RuleFlags, MatchVersion | Ranked suggestions and explanations. |
| AgentRun | RunId, SourceCourseId, CacheKey (indexed), Source (ApprovedRule / Cache / NewRun), GraphVersion, PromptVersion, Model, InputHash, OutputJson, LatencyMs, Status, CreatedAt | Evidence of AI usage, reproducibility and cache hits. |
| Evaluation | EvaluationId, SourceCourseId, EvaluatorId, OutcomeType, AwardedUnits, Rationale, DecisionAt, Status | Human review and final decision. |
| Document | DocumentId, CaseId (nullable until matched), DocumentType, Source (Portal / Email / StaffUpload), IntakeMessageId, FileName, ContentType, SizeBytes, Container, StorageKey, Sha256 (indexed), ScanStatus (Pending / Clean / Quarantined), ExtractionStatus, UploadedAt | Document metadata; bytes stay in private Blob storage. Checksum supports de-duplication and the extraction cache. |
| IntakeMessage | IntakeMessageId, Channel, GraphMessageId, InternetMessageId (unique), SenderAddress, Subject, ReceivedAt, CaseId (nullable), MatchStatus (Matched / Unmatched / Ambiguous), AssignedBy, ProcessedAt | One row per email (or portal submission). Stops duplicate processing and drives the triage queue. |
| MailSyncState | MailboxAddress, FolderId, DeltaLink, LastRunAt, LastStatus | Remembers where the Graph delta query stopped, so a restart does not reprocess mail. |
| AuditEvent | AuditId, ActorId, CaseId, Action, OldValue, NewValue, Timestamp, CorrelationId | Traceability for state and decision changes. |

**Relationships:** TransferCase has many SourceCourse and Document. IntakeMessage has many Document. SourceCourse has many MatchCandidate, AgentRun and Evaluation. BUCourse has many MatchCandidate and approved EquivalencyRule. Every material state change appends an AuditEvent.

## 13. API and module boundaries

### 13.1 .NET API (called by React)

| Module | Example endpoints / services | Responsibility |
| --- | --- | --- |
| Cases | POST /api/cases; GET /api/cases/{id}; GET /api/cases?status=Pending | Create case, list and filter queue, show case timeline. |
| Documents | POST /api/cases/{id}/documents; GET /api/documents/{id}/metadata; GET /api/documents/{id}/link | Portal upload with validation, checksum check and quarantine. Metadata and scan status. Short-lived viewing link (user delegation SAS). |
| Intake | GET /api/intake?status=Unmatched; POST /api/intake/{id}/assign; POST /api/intake/import-eml (fallback) | Triage queue for emails that could not be matched, assignment to a case, and the email simulator. The Graph polling worker runs inside the API and has no public endpoint. |
| Courses | POST /api/cases/{id}/courses/import; GET /api/courses/{id} | Import synthetic/sample transcript data and validate fields. |
| Matching | POST /api/courses/{id}/match; GET /api/courses/{id}/candidates | Approved-equivalency lookup, then cache check, then policy flags, then the agent service only if needed. Store candidates and the agent run. |
| Evaluation | POST /api/evaluations; POST /api/evaluations/{id}/finalize | Record evaluator choice, rationale, awarded units and audit. Finalising also saves an approved EquivalencyRule for reuse. |
| Rules / catalogue | GET /api/catalogue/search; admin-only rule management | Search reference data and maintain versioned rules. |
| Status | GET /api/cases/{id}/status | Expose safe, student-facing status and next action. |
| Health | GET /health; GET /api/health/agent; GET /api/health/intake | Liveness, agent connectivity, and a check that Blob Storage is reachable and the mailbox is reachable or disabled by configuration. |

### 13.2 Agent service (called only by the .NET API)

| Module | Endpoints | Responsibility |
| --- | --- | --- |
| Evaluate | POST /agent/evaluate | Run the graph for one course. Returns a structured result, or a paused state awaiting review. |
| Extract | POST /agent/extract | Extract course fields from transcript or syllabus text. |
| Health | GET /health | Liveness for the container app and the smoke test. |

Endpoint names are recommendations, not existing APIs. Protect each endpoint with authorisation and validate every request model.

### 13.3 Shared JSON contract

Agree on this shape on day one and keep it as a schema file in /contracts. Pydantic models (Python) and C# records must both match it, so the two teams can work in parallel against a stub.

```
// Request: .NET API -> agent service
{
  "courseId": "c-1042",
  "sourceCourse": {
    "code": "CS 101", "title": "Intro Programming", "credits": 4,
    "creditSystem": "quarter", "grade": "B+", "term": "Fall 2025"
  },
  "syllabusText": "...",
  "policyFlags": {
    "buOnly": false, "gradeBelowMinimum": false, "convertedCredits": 2.66
  },
  "candidates": []
}

// Response: agent service -> .NET API
{
  "matchLevel": "Partial - syllabus review",
  "suggestedBuCourse": "CAS CS 111",
  "confidence": "medium",
  "evidence": [
    { "source": "syllabus", "snippet": "...", "matchesOutcome": "..." }
  ],
  "explanation": "...",
  "studentMessage": "Your syllabus is under department review.",
  "needsHumanReview": true,
  "graphVersion": "1.0", "promptVersion": "p-007",
  "cacheKey": "sha256:..."
}
```

**Calling pattern:** the API uses a typed HttpClient with a timeout, answers the UI with 202 Accepted, processes in the background and lets the UI poll for status. The agent output and prompt version are stored in AgentRun every time.

## 14. Delivery roadmap

![road](images/road.png)

*Figure 5. Delivery phases, from sponsor validation to the live demo.*

| Phase | Activities | Deliverable / exit gate |
| --- | --- | --- |
| 0. Discovery and sponsor validation | Submit the eight discovery questions (question 2 confirms how documents really arrive). Request the Exchange admin step for the mailbox scope early. Confirm workflow, approved-equivalency data, grade policy, deadline interpretation, sample files and what "posting" means. | Answers logged; assumptions list; baseline measurement plan. |
| 1. Scope and demo data | Fix the six-course demo: 3 known matches, 2 maybe/partial, 1 quarter-system course. Create labelled synthetic cases. Load the workbook into Azure SQL. | Fixed demo dataset and acceptance checklist. |
| 2. Domain model, rules and contract | Build entities, status enums, credit conversion, grade and program rule checks, audit events and migrations. Write and commit the JSON contract. Stub the agent service. | Unit tests for all deterministic rules; sample case persisted; stub agent reachable from the API. |
| 3. Core workflow: React UI and .NET API | Case list and detail, course table, missing-evidence and deadline indicators. Portal upload to Blob Storage with validation, checksum de-duplication and scan status. Set up the Azure Pipeline and deploy the first version early. | End-to-end case creation and course ingestion on the deployed URL; a file uploaded in the portal appears on the case as Clean. |
| 4. Agent service and matching MVP | Replace the stub with the LangGraph graph: Document Intelligence, AI Search index of BU courses, matching and evidence agents, policy gate. Exact lookup and cache check first, ranking second. In parallel, the .NET track builds the email intake worker: Graph delta polling, attachment download, case matching and the triage queue, against a test mailbox. | Known matches resolved from reference data; repeat courses reuse cached results; uncertain cases routed to review; AgentRun records stored. A synthetic email with a syllabus lands on the right case or in the triage queue. |
| 5. Evaluator workbench | Side-by-side source and BU course comparison, syllabus and evidence view, approve/edit/reject, rationale capture, graph interrupt resumed by the evaluator. Finalising saves an approved equivalency. | No final outcome without an authorised human decision; audit record created; approved rule saved for reuse. |
| 6. Status and reporting | Timeline, next action, overdue indicator, queue filters and basic metrics. | Status view clearly separates pending from awarded credit. |
| 7. Test, deploy and demo | Run xUnit, pytest, Playwright and the evaluation run. Check access control and upload rules. Rehearse the six-course demo. Record limitations and measured results. | Working deployed demo, test evidence, demo script and known-gaps register. |

### Suggested team split

| Team | Builds |
| --- | --- |
| .NET developers | React UI, ASP.NET API, Azure SQL, portal upload and Blob storage, email intake worker (Graph), triage queue, policy rules, audit log, pipeline. |
| Python developer (or most comfortable person) | FastAPI service, LangGraph graph, prompts, AI Search index, evaluation script. |
| Everyone | Shared JSON contract, discovery questions, end-to-end demo flow, test evidence. |

**Timing note:** the phases are a sequence, not a committed estimate. Adjust to the real event length, team size and sponsor availability. Deploy in Phase 3, not Phase 7, so the demo never depends on a last-minute first deployment.

## 15. MVP scope vs. later scope

| MVP (build first) | Later / only after validation |
| --- | --- |
| Seed catalogue from the workbook; keep Yes/Check flags and source links. | Large-scale ingestion and sync of official catalogues. |
| Exact lookup of existing approved or sample mappings, when such mappings are supplied. | Automated integration with BU equivalency systems. |
| Rule-based credit conversion and configurable grade/program checks in C#. | Full policy authoring UI and effective-date management. |
| Reuse of approved equivalencies and cached suggestions; save approved outcomes on finalise. | Cross-institution reuse rules, expiry reviews and bulk re-validation. |
| LangGraph pipeline with transparent evidence; manual syllabus review always available. | Advanced reranking and fine-tuning after evaluation data and privacy approval. |
| Synthetic transcript cases; portal upload to Blob with validation, scan and checksum de-duplication. | Student SSO, production case portal and enterprise identity; direct-to-Blob SAS upload. |
| Email intake by polling a test mailbox with synthetic emails (or the .eml simulator); triage queue. | Change-notification webhooks with subscription renewal; the real BU mailbox; automatic acknowledgement emails (Mail.Send). |
| Simulated final posting and status update. | Direct MyBU Student write-back, only with a sponsor-approved API, security review and testing. |

## 16. Test strategy and acceptance criteria

| Test area | Representative test | Acceptance criteria |
| --- | --- | --- |
| Rules unit tests (xUnit) | Quarter 4 credits gives 2.66; semester credits unchanged; grade below threshold flagged. | Outputs match approved policy and rounding convention. |
| Program rule tests (xUnit) | A course marked Must be taken at BU is never suggested as an approved transfer. | UI shows the rule and source; evaluator cannot bypass it without an authorised override policy. |
| Agent tests (pytest) | Graph routes correctly for exact match, BU-only, missing syllabus; output validates against the schema; model calls mocked. | All paths reach the expected node; invalid output is rejected. |
| Match evaluation run | Run the past decisions (or labelled synthetic cases) through the pipeline and compare with the human outcome. | Agreement table produced and kept as evidence. Report honestly where it disagrees. |
| Reuse tests (xUnit + pytest) | Run a second synthetic student with the same institution, program and courses; change one input (prompt version, syllabus, program). | Second run makes zero AI calls; grade and credits are recomputed per student; changing any key input forces a new run; cached suggestion still requires review. |
| Workflow tests | Upload/import, match, review, decision, status update. | Every state transition is valid and audit logged. |
| Security tests | Student role requests another case or an evaluator-only note. | Request denied; no cross-case leakage. |
| Intake tests (xUnit) | Upload a PDF in the portal; send a synthetic email with a syllabus; send the same file twice; send a wrong file type; send an email with no case reference; send a test file that the scanner flags (EICAR test string). | Valid files become Clean and attach to the right case; duplicates are not stored again; bad types are rejected; unmatched email lands in triage; flagged file is quarantined and never extracted. |
| Mailbox scope test | Run the Exchange test for the app against the intake mailbox and against a different mailbox. | Access allowed for the intake mailbox and denied for any other. |
| Data quality tests | Catalogue row has verification status Check or a missing source. | Candidate clearly flagged as unverified. |
| Failure tests | Agent service, OCR or model unavailable or slow. | Manual workflow and exact lookup continue; failure visible and logged. |
| End-to-end (Playwright) | Six-course scenario through the deployed UI. | Expected paths and labels visible; nothing presented as final approval. |
| Demo acceptance | Run the six-course scenario from the brief. | 3 existing matches, 2 maybe/review cases, 1 quarter course; reviewer decision and status timeline shown. |

## 17. Discovery questions to ask the sponsor

Eight questions, chosen because each one changes what is built or what can be shown in the demo. Items marked † in the brief are covered by questions 2 to 4.

| # | Category | Question | Decision affected | Priority |
| --- | --- | --- | --- | --- |
| 1 | Business numbers / KPIs | How many transfer courses does Credit Evaluation process per month or term, and what is the average time from transcript received to posted result? How long do "maybe" courses take? | Baseline for the measurable outcome and the before/after claim. | High |
| 2 | Process | When a course is marked "maybe", how does the student send the syllabus today (email, portal, in person), and in what formats? | Whether portal upload, email intake or both are needed, and what file formats the reader must handle. | High |
| 3 | User experience | When a course sits at zero units in MyBU Student, what does the student actually see? Is there any status message? | What the student status view replaces; the core of "transparent". | High |
| 4 | Decision rules | What do evaluators look at to approve a match (description, outcomes, credit hours, grade)? Is there a rubric, or is it judgment? | The evidence the tool must show. | High |
| 5 | Decision rules / AI | The department makes the final decision. Can the tool show a suggested outcome with evidence, or only the evidence? What would make an evaluator trust or reject a suggestion? | The limit on what the tool can say and how matches are explained. | High |
| 6 | Business rules | For courses that must be taken at BU (such as CS 320, 332, 350), if a peer course looks like a strong match, should the tool block it or flag it with a warning? | Guardrails and a strong demo moment. | High |
| 7 | Data | Are the past course decisions in the sample data a fair picture of how evaluators really decide? And do they say why each decision was made? | Whether the past decisions can serve as a test set, and whether reasons can be learned. | Medium |
| 8 | Business rules | When elective credit is awarded, how are the units decided (original credit hours, or entered by the evaluator)? Are quarter conversions rounded in any set way? | Correct unit counts in the elective and quarter-system demo cases. | Medium |

Before submitting question 7, check whether the past-decisions file already has a reason column. If it does, drop the question and save the slot.

## 18. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| False equivalency from similar course titles | Compare outcomes, topics, level, credit hours and syllabus; require human review for uncertain cases. |
| Unverified catalogue data | Expose verification state and source; prioritise records marked Check for sponsor confirmation. |
| AI hallucination or unsupported evidence | Require cited snippets from source documents; allow "insufficient evidence"; no auto-decision. |
| Prompt injection through syllabus text | Treat document text as data; strict output schema; no tool access for the matching agent beyond retrieval. |
| Privacy leakage | Synthetic records for the demo, private storage, access controls, minimum necessary data, approved AI processing only. |
| Incorrect policy assumptions | Keep an assumptions register; sponsor validates each rule; version rules and add regression tests. |
| Malicious or unreadable attachment | Type, size and signature checks; quarantine container; Defender for Storage scan before any use; nothing is extracted until the scan is clean. |
| Email matched to the wrong case | Match only on an exact case reference or a known sender; anything else goes to the triage queue; staff assignment is recorded in the audit log. |
| Mailbox permission too broad or not approved in time | Scope Mail.Read to one mailbox with Exchange RBAC for Applications; ask IT early; fall back to the .eml simulator for the demo. |
| Duplicate or flooded intake | De-duplicate by internetMessageId and file checksum; size and rate limits; delta link stored so restarts do not reprocess mail. |
| Reusing a wrong or outdated result | A cache key that includes syllabus, program, rules and prompt versions; program-scoped rules; recomputing all per-student values; expiry dates can be added later. |
| Two-service integration problems | Commit the JSON contract first; build against a stub; add a health check that tests API-to-agent connectivity. |
| Agent service slow or unavailable at demo time | Timeouts, background processing and status polling; manual and exact-lookup paths keep working; rehearse with cached results as a fallback. |
| Team splits across .NET and Python | Clear ownership per service; shared contract and repo; daily integration check on the deployed environment. |
| Integration unavailable | Simulate MyBU posting and label it clearly as a demo action. |
| Demo over-scoped | Deliver exact lookup, rules and the reviewer flow first; add document intelligence only after the core workflow works. |

## 19. Suggested repository structure

```
/src
  /TransferCredit.Api              # ASP.NET Core 8 API (serves the built React app)
  /TransferCredit.Application      # Use cases, DTOs, validation, workflow services
  /TransferCredit.Domain           # Entities, enums, business rules
  /TransferCredit.Infrastructure
    /Persistence                   # EF Core, Azure SQL repositories
    /Documents                     # Blob storage
    /AgentClient                   # Typed HTTP client for the agent service
    /Intake                        # GraphMailClient, IntakePoller, file validation, scan status
/web                               # React (Vite) front end
/agent                             # Python FastAPI + LangGraph service
  /app  /graph  /prompts  /tests  /eval
/contracts                         # Shared JSON schema (single source of truth)
/tests
  /TransferCredit.UnitTests
  /TransferCredit.IntegrationTests
  /e2e                             # Playwright
/data
  /seed                            # Sanitised workbook-derived catalogue data
  /synthetic                       # Clearly labelled synthetic demo cases
/docker                            # api.Dockerfile
/docs                              # architecture, decisions, demo
/pipelines                         # shared pipeline templates
azure-pipelines.yml
TransferCredit.sln
```

## 20. Demo script: six-course scenario

1. Create a synthetic transfer case and select the target BU program.
2. Add documents both ways: upload a synthetic transcript in the portal, and email a synthetic syllabus to the test intake mailbox. Show the file arriving, the scan result and the timeline entry. Show one email with no case reference landing in the triage queue and a staff member assigning it.
3. Show the extracted fields for the six-course transcript.
4. Show three courses that match existing reference mappings, with the mapping source and verification state.
5. Show two courses labelled "maybe" or "Partial - syllabus review". Run the agent pipeline and show the candidates, the cited evidence and the missing syllabus request.
6. Show one quarter-system course and the conversion (4 quarter credits to 2.66 BU credits), calculated by the rules engine.
7. Show a BU-only course being blocked, with the rule and its source.
8. Open the evaluator workbench, compare details and evidence, and choose equivalent, elective units or no credit for each reviewed course. The graph resumes only after this decision.
9. Show the final reviewer, rationale, timestamp and audit event, plus the stored agent run.
10. Run a second synthetic student from the same university and program with the same courses. Show the results appearing instantly, labelled "Reused approved equivalency, no AI call", with grade and credits recomputed for this student.
11. Show the student status timeline and deadline indicator without internal notes.
12. Finish with the pipeline run, test results, the match-accuracy table, assumptions and known limitations.

## 21. Assumptions and open items

- The workbook's course matches are preliminary and are not official equivalencies; final BU decisions remain with evaluators.
- The workbook covers five program areas and four match levels; records marked Check need confirmation before use.
- No MyBU Student API or production access is established by the uploaded documents.
- Separate sample transcripts, syllabus files and an approved equivalency extract are not among the files reviewed; confirm availability.
- The Discovery Log row about 45 requests a month and 18 days uses placeholders and is not treated as baseline data.
- Grade rules, rounding policy, deadline interpretation, retention policy and approved AI provider remain sponsor-confirmation items.
- Org-provided LangChain and LangGraph are assumed to be Python; confirm that the hybrid design is allowed and that LangSmith is available.
- Reuse rule for now: if everything in the cache key is the same, reuse the stored result. An expiry period for approved equivalencies is not set; the sponsor can add one later.
- Students send documents by email as well as through the portal. This is an assumption until discovery question 2 is answered.
- A test mailbox and an Exchange administrator are available to scope Mail.Read to it. If not, the .eml simulator replaces live email in the demo.
- Retention period for raw files and email metadata is not set; confirm with the sponsor.
- Azure OpenAI, Document Intelligence and AI Search access in the team subscription is assumed; confirm quotas and region.

## 22. Reference material

- Boston University.docx: FDE AI Hackathon 5.0 problem statement, current workflow, rules, sample-data description and six-course demo scenario.
- BU_Transfer_Program_Course_Match.xlsx: README, Programs, BU/Northeastern/NYU course sheets, Course Match, Match Summary and Transfer Lookup.
- Hackathon_Discovery_Log.xlsx: question log template; the visible example row must be verified before use as a baseline.
- EvoHack 2026 kickoff slide: Level 1 qualification must-have checklist.
- Microsoft Learn pages for Graph mail, attachments, change notifications, Exchange RBAC for Applications, Blob Storage and Defender for Storage (links in section 8.2).
- Companion document: Azure DevOps CI/CD Deployment Guide (React, ASP.NET Core 8, Python agent service).

This is a proposed implementation plan derived from the supplied material. It is not an official Boston University policy document and must be validated with the sponsor before any real student records or operational decisions are involved.
