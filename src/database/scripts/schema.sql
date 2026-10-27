IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [ActorId] nvarchar(64) NOT NULL,
        [CaseId] int NULL,
        [Action] nvarchar(100) NOT NULL,
        [Detail] nvarchar(max) NULL,
        [OldValue] nvarchar(max) NULL,
        [NewValue] nvarchar(max) NULL,
        [StudentMessage] nvarchar(500) NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [CorrelationId] nvarchar(100) NULL,
        CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [BUCourses] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Program] nvarchar(100) NOT NULL,
        [RequirementType] nvarchar(100) NOT NULL,
        [Credits] decimal(9,2) NOT NULL,
        [Description] nvarchar(max) NULL,
        [VerifiedStatus] nvarchar(40) NOT NULL,
        [SourceUrl] nvarchar(500) NULL,
        [EffectiveDate] date NOT NULL,
        CONSTRAINT [PK_BUCourses] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [Cases] (
        [Id] int NOT NULL IDENTITY,
        [CaseRef] nvarchar(20) NOT NULL,
        [DemoStudentRef] nvarchar(64) NOT NULL,
        [StudentName] nvarchar(200) NOT NULL,
        [StudentEmail] nvarchar(320) NULL,
        [Program] nvarchar(100) NOT NULL,
        [StartDate] date NOT NULL,
        [DeadlineDate] date NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [PostedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Cases] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [DemoUsers] (
        [Id] nvarchar(64) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [Role] nvarchar(40) NOT NULL,
        [Email] nvarchar(max) NULL,
        [StudentRef] nvarchar(64) NULL,
        CONSTRAINT [PK_DemoUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [Institutions] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [CreditSystem] nvarchar(40) NOT NULL,
        [CatalogueSource] nvarchar(max) NULL,
        [VerifiedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Institutions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [IntakeMessages] (
        [Id] int NOT NULL IDENTITY,
        [Channel] nvarchar(40) NOT NULL,
        [GraphMessageId] nvarchar(500) NULL,
        [InternetMessageId] nvarchar(500) NOT NULL,
        [SenderAddress] nvarchar(320) NOT NULL,
        [Subject] nvarchar(500) NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [CaseId] int NULL,
        [MatchStatus] nvarchar(40) NOT NULL,
        [AssignedBy] nvarchar(64) NULL,
        [ProcessedAt] datetimeoffset NULL,
        CONSTRAINT [PK_IntakeMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [MailSyncStates] (
        [MailboxAddress] nvarchar(320) NOT NULL,
        [FolderId] nvarchar(100) NOT NULL,
        [DeltaLink] nvarchar(max) NULL,
        [LastRunAt] datetimeoffset NULL,
        [LastStatus] nvarchar(500) NULL,
        CONSTRAINT [PK_MailSyncStates] PRIMARY KEY ([MailboxAddress], [FolderId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [RuleSettings] (
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        CONSTRAINT [PK_RuleSettings] PRIMARY KEY ([Key])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [ProgramRules] (
        [Id] int NOT NULL IDENTITY,
        [Program] nvarchar(100) NOT NULL,
        [BUCourseId] int NOT NULL,
        [RuleType] nvarchar(40) NOT NULL,
        [Source] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_ProgramRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProgramRules_BUCourses_BUCourseId] FOREIGN KEY ([BUCourseId]) REFERENCES [BUCourses] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [EquivalencyRules] (
        [Id] int NOT NULL IDENTITY,
        [InstitutionId] int NOT NULL,
        [SourceCode] nvarchar(40) NOT NULL,
        [SourceTitle] nvarchar(200) NOT NULL,
        [Program] nvarchar(100) NOT NULL,
        [OutcomeType] nvarchar(40) NOT NULL,
        [BUCourseId] int NULL,
        [ApprovalStatus] nvarchar(40) NOT NULL,
        [VerifiedStatus] nvarchar(40) NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [DecisionSource] nvarchar(200) NOT NULL,
        [CreatedFromEvaluationId] int NULL,
        [CacheKey] nvarchar(100) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_EquivalencyRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquivalencyRules_BUCourses_BUCourseId] FOREIGN KEY ([BUCourseId]) REFERENCES [BUCourses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_EquivalencyRules_Institutions_InstitutionId] FOREIGN KEY ([InstitutionId]) REFERENCES [Institutions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [Documents] (
        [Id] int NOT NULL IDENTITY,
        [CaseId] int NULL,
        [SourceCourseId] int NULL,
        [DocumentType] nvarchar(40) NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [IntakeMessageId] int NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Container] nvarchar(40) NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [ScanStatus] nvarchar(40) NOT NULL,
        [ExtractionStatus] nvarchar(40) NOT NULL,
        [UploadedBy] nvarchar(64) NULL,
        [UploadedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Documents_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_Documents_IntakeMessages_IntakeMessageId] FOREIGN KEY ([IntakeMessageId]) REFERENCES [IntakeMessages] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [SourceCourses] (
        [Id] int NOT NULL IDENTITY,
        [CaseId] int NOT NULL,
        [InstitutionId] int NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Credits] decimal(9,2) NOT NULL,
        [CreditSystem] nvarchar(40) NOT NULL,
        [ConvertedCredits] decimal(9,2) NOT NULL,
        [Grade] nvarchar(5) NULL,
        [Term] nvarchar(40) NULL,
        [Description] nvarchar(max) NULL,
        [SyllabusText] nvarchar(max) NULL,
        [SyllabusDocumentId] int NULL,
        [Status] nvarchar(40) NOT NULL,
        [MatchLevel] nvarchar(40) NULL,
        [AppliedEquivalencyRuleId] int NULL,
        [PolicyFlagsJson] nvarchar(max) NULL,
        CONSTRAINT [PK_SourceCourses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SourceCourses_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SourceCourses_EquivalencyRules_AppliedEquivalencyRuleId] FOREIGN KEY ([AppliedEquivalencyRuleId]) REFERENCES [EquivalencyRules] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SourceCourses_Institutions_InstitutionId] FOREIGN KEY ([InstitutionId]) REFERENCES [Institutions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [AgentRuns] (
        [Id] int NOT NULL IDENTITY,
        [SourceCourseId] int NOT NULL,
        [CacheKey] nvarchar(100) NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [GraphVersion] nvarchar(40) NULL,
        [PromptVersion] nvarchar(40) NULL,
        [Model] nvarchar(100) NULL,
        [ThreadId] nvarchar(100) NULL,
        [InputHash] nvarchar(100) NULL,
        [OutputJson] nvarchar(max) NULL,
        [Error] nvarchar(max) NULL,
        [LatencyMs] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AgentRuns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AgentRuns_SourceCourses_SourceCourseId] FOREIGN KEY ([SourceCourseId]) REFERENCES [SourceCourses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [Evaluations] (
        [Id] int NOT NULL IDENTITY,
        [SourceCourseId] int NOT NULL,
        [EvaluatorId] nvarchar(64) NOT NULL,
        [OutcomeType] nvarchar(40) NOT NULL,
        [BUCourseId] int NULL,
        [AwardedUnits] decimal(9,2) NOT NULL,
        [Rationale] nvarchar(4000) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [DecisionAt] datetimeoffset NULL,
        CONSTRAINT [PK_Evaluations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Evaluations_BUCourses_BUCourseId] FOREIGN KEY ([BUCourseId]) REFERENCES [BUCourses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Evaluations_SourceCourses_SourceCourseId] FOREIGN KEY ([SourceCourseId]) REFERENCES [SourceCourses] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE TABLE [MatchCandidates] (
        [Id] int NOT NULL IDENTITY,
        [SourceCourseId] int NOT NULL,
        [BUCourseId] int NOT NULL,
        [MatchLevel] nvarchar(40) NOT NULL,
        [Score] decimal(9,2) NULL,
        [Confidence] nvarchar(20) NULL,
        [EvidenceJson] nvarchar(max) NULL,
        [Explanation] nvarchar(max) NULL,
        [RuleFlagsJson] nvarchar(max) NULL,
        [MatchVersion] nvarchar(40) NOT NULL,
        [Source] nvarchar(40) NOT NULL,
        [IsBlocked] bit NOT NULL,
        [AgentRunId] int NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_MatchCandidates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MatchCandidates_BUCourses_BUCourseId] FOREIGN KEY ([BUCourseId]) REFERENCES [BUCourses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MatchCandidates_SourceCourses_SourceCourseId] FOREIGN KEY ([SourceCourseId]) REFERENCES [SourceCourses] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AgentRuns_CacheKey] ON [AgentRuns] ([CacheKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AgentRuns_SourceCourseId] ON [AgentRuns] ([SourceCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditEvents_CaseId] ON [AuditEvents] ([CaseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BUCourses_Code_Program] ON [BUCourses] ([Code], [Program]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cases_CaseRef] ON [Cases] ([CaseRef]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Cases_DemoStudentRef] ON [Cases] ([DemoStudentRef]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Cases_Status] ON [Cases] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Cases_StudentEmail] ON [Cases] ([StudentEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_CaseId] ON [Documents] ([CaseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_IntakeMessageId] ON [Documents] ([IntakeMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_ScanStatus] ON [Documents] ([ScanStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_Sha256] ON [Documents] ([Sha256]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_EquivalencyRules_BUCourseId] ON [EquivalencyRules] ([BUCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_EquivalencyRules_InstitutionId_SourceCode_Program] ON [EquivalencyRules] ([InstitutionId], [SourceCode], [Program]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Evaluations_BUCourseId] ON [Evaluations] ([BUCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Evaluations_SourceCourseId] ON [Evaluations] ([SourceCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Institutions_Name] ON [Institutions] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IntakeMessages_InternetMessageId] ON [IntakeMessages] ([InternetMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IntakeMessages_MatchStatus] ON [IntakeMessages] ([MatchStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MatchCandidates_BUCourseId] ON [MatchCandidates] ([BUCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MatchCandidates_SourceCourseId] ON [MatchCandidates] ([SourceCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProgramRules_BUCourseId] ON [ProgramRules] ([BUCourseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SourceCourses_AppliedEquivalencyRuleId] ON [SourceCourses] ([AppliedEquivalencyRuleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SourceCourses_CaseId] ON [SourceCourses] ([CaseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SourceCourses_InstitutionId] ON [SourceCourses] ([InstitutionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026133512_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026133512_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100231_AddLearningOutcomesAndSourceCatalogue'
)
BEGIN
    ALTER TABLE [SourceCourses] ADD [LearningOutcomes] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100231_AddLearningOutcomesAndSourceCatalogue'
)
BEGIN
    ALTER TABLE [BUCourses] ADD [LearningOutcomes] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100231_AddLearningOutcomesAndSourceCatalogue'
)
BEGIN
    CREATE TABLE [SourceCatalogueCourses] (
        [Id] int NOT NULL IDENTITY,
        [InstitutionId] int NOT NULL,
        [Code] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Credits] decimal(9,2) NOT NULL,
        [Description] nvarchar(max) NULL,
        [LearningOutcomes] nvarchar(max) NULL,
        CONSTRAINT [PK_SourceCatalogueCourses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SourceCatalogueCourses_Institutions_InstitutionId] FOREIGN KEY ([InstitutionId]) REFERENCES [Institutions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100231_AddLearningOutcomesAndSourceCatalogue'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SourceCatalogueCourses_InstitutionId_Code] ON [SourceCatalogueCourses] ([InstitutionId], [Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027100231_AddLearningOutcomesAndSourceCatalogue'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027100231_AddLearningOutcomesAndSourceCatalogue', N'8.0.11');
END;
GO

COMMIT;
GO

