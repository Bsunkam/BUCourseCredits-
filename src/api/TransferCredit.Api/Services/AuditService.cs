using System.Diagnostics;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Services;

/// <summary>Appends audit events. The caller saves them with its own unit of work.</summary>
public class AuditService(TransferCreditDbContext db, TimeProvider clock)
{
    public void Record(
        string actorId, int? caseId, string action, string? detail = null,
        string? oldValue = null, string? newValue = null, string? studentMessage = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorId = actorId,
            CaseId = caseId,
            Action = action,
            Detail = detail,
            OldValue = oldValue,
            NewValue = newValue,
            StudentMessage = studentMessage,
            Timestamp = clock.GetUtcNow(),
            CorrelationId = Activity.Current?.TraceId.ToString()
        });
    }
}

public static class AuditActions
{
    public const string CaseCreated = "CaseCreated";
    public const string CourseImported = "CourseImported";
    public const string SyllabusUpdated = "SyllabusUpdated";
    public const string DocumentReceived = "DocumentReceived";
    public const string DocumentDuplicate = "DocumentDuplicate";
    public const string DocumentClean = "DocumentClean";
    public const string DocumentQuarantined = "DocumentQuarantined";
    public const string DocumentExtracted = "DocumentExtracted";
    public const string IntakeTriaged = "IntakeTriaged";
    public const string IntakeAssigned = "IntakeAssigned";
    public const string MatchQueued = "MatchQueued";
    public const string MatchCompleted = "MatchCompleted";
    public const string AgentFailed = "AgentFailed";
    public const string SyllabusRequested = "SyllabusRequested";
    public const string EvaluationDrafted = "EvaluationDrafted";
    public const string EvaluationFinalised = "EvaluationFinalised";
    public const string EquivalencySaved = "EquivalencySaved";
    public const string EquivalencyNotSaved = "EquivalencyNotSaved";
    public const string EquivalencyApplied = "EquivalencyApplied";
    public const string CasePosted = "CasePostedSimulated";
    public const string UserSignedIn = "UserSignedIn";
    public const string UserSignedOut = "UserSignedOut";
    public const string SignInFailed = "SignInFailed";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserImported = "UserImported";
}
