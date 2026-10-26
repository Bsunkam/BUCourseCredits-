using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferCredit.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StudentMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BUCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Program = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequirementType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Credits = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifiedStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUCourses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseRef = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DemoStudentRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StudentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StudentEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Program = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeadlineDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DemoUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StudentRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Institutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreditSystem = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CatalogueSource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntakeMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GraphMessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InternetMessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SenderAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailSyncStates",
                columns: table => new
                {
                    MailboxAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FolderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeltaLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailSyncStates", x => new { x.MailboxAddress, x.FolderId });
                });

            migrationBuilder.CreateTable(
                name: "RuleSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ProgramRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Program = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BUCourseId = table.Column<int>(type: "int", nullable: false),
                    RuleType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramRules_BUCourses_BUCourseId",
                        column: x => x.BUCourseId,
                        principalTable: "BUCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquivalencyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(type: "int", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Program = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OutcomeType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BUCourseId = table.Column<int>(type: "int", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VerifiedStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DecisionSource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedFromEvaluationId = table.Column<int>(type: "int", nullable: true),
                    CacheKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquivalencyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquivalencyRules_BUCourses_BUCourseId",
                        column: x => x.BUCourseId,
                        principalTable: "BUCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquivalencyRules_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<int>(type: "int", nullable: true),
                    SourceCourseId = table.Column<int>(type: "int", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IntakeMessageId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Container = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScanStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExtractionStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documents_IntakeMessages_IntakeMessageId",
                        column: x => x.IntakeMessageId,
                        principalTable: "IntakeMessages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SourceCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    InstitutionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Credits = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    CreditSystem = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ConvertedCredits = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Term = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyllabusText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyllabusDocumentId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MatchLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AppliedEquivalencyRuleId = table.Column<int>(type: "int", nullable: true),
                    PolicyFlagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceCourses_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SourceCourses_EquivalencyRules_AppliedEquivalencyRuleId",
                        column: x => x.AppliedEquivalencyRuleId,
                        principalTable: "EquivalencyRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceCourses_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceCourseId = table.Column<int>(type: "int", nullable: false),
                    CacheKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GraphVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ThreadId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InputHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OutputJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuns_SourceCourses_SourceCourseId",
                        column: x => x.SourceCourseId,
                        principalTable: "SourceCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceCourseId = table.Column<int>(type: "int", nullable: false),
                    EvaluatorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutcomeType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BUCourseId = table.Column<int>(type: "int", nullable: true),
                    AwardedUnits = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecisionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evaluations_BUCourses_BUCourseId",
                        column: x => x.BUCourseId,
                        principalTable: "BUCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Evaluations_SourceCourses_SourceCourseId",
                        column: x => x.SourceCourseId,
                        principalTable: "SourceCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceCourseId = table.Column<int>(type: "int", nullable: false),
                    BUCourseId = table.Column<int>(type: "int", nullable: false),
                    MatchLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Confidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RuleFlagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    AgentRunId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchCandidates_BUCourses_BUCourseId",
                        column: x => x.BUCourseId,
                        principalTable: "BUCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCandidates_SourceCourses_SourceCourseId",
                        column: x => x.SourceCourseId,
                        principalTable: "SourceCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_CacheKey",
                table: "AgentRuns",
                column: "CacheKey");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_SourceCourseId",
                table: "AgentRuns",
                column: "SourceCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CaseId",
                table: "AuditEvents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_BUCourses_Code_Program",
                table: "BUCourses",
                columns: new[] { "Code", "Program" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_CaseRef",
                table: "Cases",
                column: "CaseRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_DemoStudentRef",
                table: "Cases",
                column: "DemoStudentRef");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Status",
                table: "Cases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_StudentEmail",
                table: "Cases",
                column: "StudentEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CaseId",
                table: "Documents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IntakeMessageId",
                table: "Documents",
                column: "IntakeMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ScanStatus",
                table: "Documents",
                column: "ScanStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Sha256",
                table: "Documents",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_EquivalencyRules_BUCourseId",
                table: "EquivalencyRules",
                column: "BUCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_EquivalencyRules_InstitutionId_SourceCode_Program",
                table: "EquivalencyRules",
                columns: new[] { "InstitutionId", "SourceCode", "Program" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_BUCourseId",
                table: "Evaluations",
                column: "BUCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_SourceCourseId",
                table: "Evaluations",
                column: "SourceCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_Name",
                table: "Institutions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeMessages_InternetMessageId",
                table: "IntakeMessages",
                column: "InternetMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeMessages_MatchStatus",
                table: "IntakeMessages",
                column: "MatchStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCandidates_BUCourseId",
                table: "MatchCandidates",
                column: "BUCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCandidates_SourceCourseId",
                table: "MatchCandidates",
                column: "SourceCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramRules_BUCourseId",
                table: "ProgramRules",
                column: "BUCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCourses_AppliedEquivalencyRuleId",
                table: "SourceCourses",
                column: "AppliedEquivalencyRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCourses_CaseId",
                table: "SourceCourses",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCourses_InstitutionId",
                table: "SourceCourses",
                column: "InstitutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "DemoUsers");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Evaluations");

            migrationBuilder.DropTable(
                name: "MailSyncStates");

            migrationBuilder.DropTable(
                name: "MatchCandidates");

            migrationBuilder.DropTable(
                name: "ProgramRules");

            migrationBuilder.DropTable(
                name: "RuleSettings");

            migrationBuilder.DropTable(
                name: "IntakeMessages");

            migrationBuilder.DropTable(
                name: "SourceCourses");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropTable(
                name: "EquivalencyRules");

            migrationBuilder.DropTable(
                name: "BUCourses");

            migrationBuilder.DropTable(
                name: "Institutions");
        }
    }
}
