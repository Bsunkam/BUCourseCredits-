using Microsoft.EntityFrameworkCore;
using TransferCredit.Api.Contracts;
using TransferCredit.Api.Rules;
using TransferCredit.Data;
using TransferCredit.Data.Entities;
using TransferCredit.Data.Seed;

namespace TransferCredit.Api.Services;

public class ValidationFailedException(string message) : Exception(message);

public class CaseService(TransferCreditDbContext db, RuleSettingsService rules, AuditService audit, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<TransferCase> CreateAsync(CreateCaseRequest request, string actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudentRef) || string.IsNullOrWhiteSpace(request.StudentName))
            throw new ValidationFailedException("Student reference and name are required.");
        if (string.IsNullOrWhiteSpace(request.Program))
            throw new ValidationFailedException("Program is required.");

        var settings = await rules.GetAsync(ct);
        var c = new TransferCase
        {
            CaseRef = await NextCaseRefAsync(ct),
            DemoStudentRef = request.StudentRef.Trim(),
            StudentName = request.StudentName.Trim(),
            StudentEmail = request.StudentEmail?.Trim().ToLowerInvariant(),
            Program = request.Program.Trim(),
            StartDate = request.StartDate,
            DeadlineDate = RulesEngine.Deadline(request.StartDate, settings.DeadlineMonths),
            Status = CaseStatus.Open,
            CreatedAt = clock.GetUtcNow()
        };
        db.Cases.Add(c);
        await db.SaveChangesAsync(ct);
        audit.Record(actorId, c.Id, AuditActions.CaseCreated, $"Case {c.CaseRef} created for {c.Program}.",
            studentMessage: $"Transfer credit case {c.CaseRef} opened. Deadline to complete transfer: {c.DeadlineDate:yyyy-MM-dd}.");
        await db.SaveChangesAsync(ct);
        return c;
    }

    private async Task<string> NextCaseRefAsync(CancellationToken ct)
    {
        var refs = await db.Cases.Select(c => c.CaseRef).ToListAsync(ct);
        var max = refs.Select(r => int.TryParse(r.Replace("TC-", ""), out var n) ? n : 0).DefaultIfEmpty(1041).Max();
        return $"TC-{Math.Max(max, 1041) + 1}";
    }

    public async Task<List<SourceCourse>> ImportCoursesAsync(int caseId, ImportCoursesRequest request, string actorId, CancellationToken ct)
    {
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct)
            ?? throw new KeyNotFoundException($"Case {caseId} not found.");
        if (request.Courses is not { Count: > 0 }) throw new ValidationFailedException("No courses supplied.");

        var institutionIds = request.Courses.Select(x => x.InstitutionId).Distinct().ToList();
        var institutions = await db.Institutions.Where(i => institutionIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        // Public catalogue entries give the evaluator a description and outcomes before any syllabus arrives.
        var published = await db.SourceCatalogueCourses.AsNoTracking()
            .Where(x => institutionIds.Contains(x.InstitutionId)).ToListAsync(ct);

        var added = new List<SourceCourse>();
        foreach (var item in request.Courses)
        {
            if (!institutions.TryGetValue(item.InstitutionId, out var institution))
                throw new ValidationFailedException($"Unknown institution {item.InstitutionId}.");
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Title))
                throw new ValidationFailedException("Course code and title are required.");
            if (item.Credits is <= 0 or > 20)
                throw new ValidationFailedException($"{item.Code}: credits must be between 0 and 20.");

            var entry = published.FirstOrDefault(x => x.InstitutionId == institution.Id
                                                      && RulesEngine.NormaliseCode(x.Code) == RulesEngine.NormaliseCode(item.Code));
            var course = new SourceCourse
            {
                CaseId = caseId,
                InstitutionId = institution.Id,
                Institution = institution,
                Code = item.Code.Trim(),
                Title = item.Title.Trim(),
                Credits = item.Credits,
                CreditSystem = institution.CreditSystem,
                ConvertedCredits = await rules.ConvertAsync(item.Credits, institution.CreditSystem, ct),
                Grade = item.Grade?.Trim().ToUpperInvariant(),
                Term = item.Term?.Trim(),
                Description = string.IsNullOrWhiteSpace(item.Description) ? entry?.Description : item.Description.Trim(),
                LearningOutcomes = string.IsNullOrWhiteSpace(item.LearningOutcomes) ? entry?.LearningOutcomes : item.LearningOutcomes.Trim(),
                SyllabusText = string.IsNullOrWhiteSpace(item.SyllabusText) ? null : item.SyllabusText.Trim(),
                Status = CourseStatus.Pending
            };
            db.SourceCourses.Add(course);
            added.Add(course);
            audit.Record(actorId, caseId, AuditActions.CourseImported,
                $"{course.Code} {course.Title}: {course.Credits} {course.CreditSystem} credits -> {course.ConvertedCredits} BU credits.",
                studentMessage: $"Course {course.Code} {course.Title} added for evaluation.");
        }

        await db.SaveChangesAsync(ct);
        await RefreshStatusAsync(c.Id, ct);
        return added;
    }

    public async Task UpdateSyllabusAsync(int courseId, string text, string actorId, CancellationToken ct)
    {
        var course = await db.SourceCourses.FirstOrDefaultAsync(c => c.Id == courseId, ct)
            ?? throw new KeyNotFoundException($"Course {courseId} not found.");
        if (course.Status == CourseStatus.Decided)
            throw new ValidationFailedException("The course already has a final decision.");

        course.SyllabusText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        // New evidence: the course must be matched again.
        course.Status = CourseStatus.Pending;
        audit.Record(actorId, course.CaseId, AuditActions.SyllabusUpdated, $"{course.Code}: syllabus text updated.",
            studentMessage: $"Syllabus received for {course.Code}.");
        await db.SaveChangesAsync(ct);
        await RefreshStatusAsync(course.CaseId, ct);
    }

    public async Task RefreshStatusAsync(int caseId, CancellationToken ct)
    {
        var c = await db.Cases.Include(x => x.Courses).FirstAsync(x => x.Id == caseId, ct);
        if (c.Status == CaseStatus.Posted) return;

        var statuses = c.Courses.Select(x => x.Status).ToList();
        c.Status = statuses.Count == 0 ? CaseStatus.Open
            : statuses.All(s => s == CourseStatus.Decided) ? CaseStatus.Decided
            : statuses.Any(s => s == CourseStatus.NeedsSyllabus) ? CaseStatus.AwaitingDocuments
            : statuses.Any(s => s != CourseStatus.Pending) ? CaseStatus.InReview
            : CaseStatus.Open;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Simulated MyBU Student posting. No live integration (design section 1).</summary>
    public async Task PostSimulatedAsync(int caseId, string actorId, CancellationToken ct)
    {
        var c = await db.Cases.Include(x => x.Courses).FirstOrDefaultAsync(x => x.Id == caseId, ct)
            ?? throw new KeyNotFoundException($"Case {caseId} not found.");
        if (c.Courses.Count == 0 || c.Courses.Any(x => x.Status != CourseStatus.Decided))
            throw new ValidationFailedException("Every course needs a final evaluator decision before posting.");
        if (c.Status == CaseStatus.Posted) throw new ValidationFailedException("Case already posted.");

        var finals = await db.Evaluations.AsNoTracking().Include(e => e.BUCourse)
            .Where(e => e.SourceCourse!.CaseId == caseId && e.Status == EvaluationStatus.Final).ToListAsync(ct);
        var posted = finals.Where(e => e.AwardedUnits > 0).ToList();

        c.Status = CaseStatus.Posted;
        c.PostedAt = clock.GetUtcNow();
        audit.Record(actorId, c.Id, AuditActions.CasePosted,
            $"SIMULATED posting to MyBU Student: {posted.Count} course(s), {posted.Sum(e => e.AwardedUnits)} units, " +
            $"grade {RulesEngine.TransferGrade}, {RulesEngine.HubUnits} Hub units, not counted in BU GPA. " +
            "No record was written to any BU system.",
            studentMessage: $"Final transfer credit results posted (demo simulation): {posted.Sum(e => e.AwardedUnits)} units " +
                            $"with a grade of {RulesEngine.TransferGrade}.");
        await db.SaveChangesAsync(ct);
    }

    public async Task<CaseDetailDto?> GetDetailAsync(int caseId, CancellationToken ct)
    {
        var c = await LoadCaseAsync(caseId, ct);
        if (c is null) return null;

        var buOnly = await BuOnlyRulesAsync(c.Program, ct);
        var timeline = await db.AuditEvents.AsNoTracking().Where(a => a.CaseId == caseId)
            .OrderByDescending(a => a.Id).Take(200).ToListAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(d => d.CaseId == caseId)
            .OrderByDescending(d => d.Id).ToListAsync(ct);

        return new CaseDetailDto(
            c.ToSummary(Today), c.StudentEmail, c.PostedAt,
            c.Courses.OrderBy(x => x.Id).Select(x => x.ToDto(buOnly, c.Status == CaseStatus.Posted)).ToList(),
            documents.Select(d => d.ToDto()).ToList(),
            timeline.Select(a => a.ToDto()).ToList());
    }

    public async Task<StudentStatusDto?> GetStudentStatusAsync(int caseId, CancellationToken ct)
    {
        var c = await LoadCaseAsync(caseId, ct);
        if (c is null) return null;

        var timeline = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CaseId == caseId && a.StudentMessage != null)
            .OrderByDescending(a => a.Id).Take(100)
            .Select(a => new TimelineEntryDto(a.Timestamp, a.StudentMessage!))
            .ToListAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(d => d.CaseId == caseId)
            .OrderByDescending(d => d.Id).ToListAsync(ct);
        var minimumGrade = (await rules.GetAsync(ct)).MinimumGradeFor(c.Program);
        var posted = c.Status == CaseStatus.Posted;

        var courses = c.Courses.OrderBy(x => x.Id).Select(x =>
        {
            var final = x.Evaluations.FirstOrDefault(e => e.Status == EvaluationStatus.Final);
            var state = x.Status switch
            {
                CourseStatus.Decided => "Decided",
                CourseStatus.NeedsSyllabus => "Syllabus needed",
                CourseStatus.Pending => "Pending",
                _ => "Under department review"
            };
            // BR-02: pending credit shows as zero units until a final decision.
            var units = final?.AwardedUnits ?? 0m;
            var outcome = final?.OutcomeType switch
            {
                OutcomeType.BuEquivalent => $"BU equivalent: {final.BUCourse?.Code} {final.BUCourse?.Title}",
                OutcomeType.ElectiveCredit => "Elective credit",
                OutcomeType.NoCredit => "No credit",
                _ => null
            };
            var (standing, reason) = Standing(x, final, minimumGrade);
            return new StudentCourseDto(x.Id, x.Code, x.Title, x.Institution?.Name ?? "", state,
                x.Status == CourseStatus.NeedsSyllabus, units, outcome, x.ConvertedCredits, standing, reason,
                final is null ? null : Requirement(final, c.Program),
                x.Status == CourseStatus.NeedsSyllabus ? SyllabusReason(x) : null,
                x.PostedGrade(posted), RulesEngine.HubUnits);
        }).ToList();

        var progress = new DegreeProgressDto(
            courses.Where(x => x.Standing == StandingSecured).Sum(x => x.AwardedUnits),
            courses.Where(x => x.Standing == StandingPending).Sum(x => x.PotentialUnits),
            courses.Where(x => x.Standing == StandingAtRisk).Sum(x => x.PotentialUnits),
            courses.Count(x => x.SyllabusNeeded),
            courses.Where(x => x.Standing == StandingSecured && x.Requirement is not null)
                .GroupBy(x => x.Requirement!)
                .Select(g => new RequirementProgressDto(g.Key, g.Sum(x => x.AwardedUnits), g.Count()))
                .OrderByDescending(g => g.Units).ToList());

        return new StudentStatusDto(
            c.Id, c.CaseRef, c.Program, c.StartDate, c.DeadlineDate,
            RulesEngine.DaysRemaining(c.DeadlineDate, Today),
            c.Status != CaseStatus.Posted && RulesEngine.DaysRemaining(c.DeadlineDate, Today) < 0,
            c.Status, NextAction(c, documents.Count), courses.Sum(x => x.AwardedUnits), 0m,
            courses, timeline, documents.Select(d => d.ToDto()).ToList(), progress, PolicyNotes(c), c.PostedAt);
    }

    public const string StandingSecured = "Secured";
    public const string StandingPending = "Pending review";
    public const string StandingAtRisk = "At risk";
    public const string StandingNoCredit = "No credit";

    /// <summary>Where a course stands for the student, in plain words. Never shows pending units as awarded.</summary>
    private static (string Standing, string? Reason) Standing(SourceCourse x, Evaluation? final, string minimumGrade)
    {
        if (final is not null)
            return final.AwardedUnits > 0
                ? (StandingSecured, null)
                : (StandingNoCredit, "The department decided this course does not transfer for credit.");
        if (x.Status == CourseStatus.Blocked)
            return (StandingAtRisk, "Your program requires the matching BU course to be taken at BU, so this course may only count as elective units or not at all.");
        if (RulesEngine.IsGradeBelowMinimum(x.Grade, minimumGrade) == true)
            return (StandingAtRisk, $"Your grade ({x.Grade}) is below the minimum ({minimumGrade}) most BU schools require for credit.");
        if (x.Status == CourseStatus.NeedsSyllabus)
            return (StandingAtRisk, "It cannot be evaluated until we receive the syllabus.");
        return (StandingPending, null);
    }

    /// <summary>The degree requirement a decided course counts toward, as far as the transfer record can tell.</summary>
    private static string Requirement(Evaluation final, string program) => final.OutcomeType switch
    {
        OutcomeType.ElectiveCredit => "General elective units",
        OutcomeType.NoCredit => "None",
        _ when final.BUCourse is null => "General elective units",
        _ when final.BUCourse.Program == program => final.BUCourse.RequirementType,
        _ when final.BUCourse.RequirementType == "Math and science requirement" => final.BUCourse.RequirementType,
        _ => "Outside the major (counts toward total units)"
    };

    private static string SyllabusReason(SourceCourse x) =>
        $"BU has no existing equivalency for {x.Code}, so a department evaluator must compare its syllabus " +
        "(topics, learning outcomes and credit hours) with BU courses. Send the syllabus for the term you took it" +
        (x.Term is null ? "" : $" ({x.Term})") +
        ". If you no longer have it, the department office or a former instructor at " +
        $"{x.Institution?.Name ?? "your previous school"} can usually provide one.";

    private static List<string> PolicyNotes(TransferCase c) =>
    [
        "Courses under review show 0 units until a department evaluator decides. Their potential units are shown separately and are not awarded yet.",
        $"Transfer credit is posted to MyBU Student with a grade of {RulesEngine.TransferGrade}. Grades from other institutions do not count toward your BU GPA.",
        "Transfer courses do not earn BU Hub general education units.",
        $"All credits must be transferred by {c.DeadlineDate:MMMM d, yyyy}, one year after you start at BU.",
        "Only secured units are final. Use the secured figure when you talk to your advisor or the financial aid office; pending units are not guaranteed."
    ];

    private static string NextAction(TransferCase c, int documentCount)
    {
        if (c.Status == CaseStatus.Posted) return "Final results have been posted (demo simulation). No action needed.";
        if (c.Courses.Count == 0)
            return documentCount == 0 ? "Upload your transcript to start the evaluation." : "Credit Evaluation is reviewing your documents.";
        var syllabus = c.Courses.Where(x => x.Status == CourseStatus.NeedsSyllabus).Select(x => x.Code).ToList();
        if (syllabus.Count > 0) return $"Send the syllabus for: {string.Join(", ", syllabus)}.";
        if (c.Courses.All(x => x.Status == CourseStatus.Decided)) return "All decisions are made. Results will be posted shortly.";
        return "Department review in progress. No action needed from you right now.";
    }

    private Task<TransferCase?> LoadCaseAsync(int caseId, CancellationToken ct) =>
        db.Cases.AsNoTracking().AsSplitQuery()
            .Include(x => x.Courses).ThenInclude(x => x.Institution)
            .Include(x => x.Courses).ThenInclude(x => x.Candidates).ThenInclude(x => x.BUCourse)
            .Include(x => x.Courses).ThenInclude(x => x.Evaluations).ThenInclude(x => x.BUCourse)
            .Include(x => x.Courses).ThenInclude(x => x.AppliedEquivalencyRule).ThenInclude(x => x!.BUCourse)
            .FirstOrDefaultAsync(x => x.Id == caseId, ct);

    public async Task<Dictionary<int, string>> BuOnlyRulesAsync(string program, CancellationToken ct) =>
        await db.ProgramRules.AsNoTracking()
            .Where(r => r.Program == program && r.RuleType == ProgramRuleType.MustBeTakenAtBu)
            .ToDictionaryAsync(r => r.BUCourseId, r => r.Source, ct);

    /// <summary>
    /// Creates a new synthetic case from a demo.json template. The default is the brief's six-course scenario
    /// (community college: 3 existing equivalencies, 2 "maybe", 1 quarter-system course).
    /// </summary>
    public async Task<TransferCase> CreateDemoCaseAsync(string studentRef, string actorId, CancellationToken ct, string? templateName = null)
    {
        var demo = SeedData.LoadDemo();
        var template = string.IsNullOrWhiteSpace(templateName)
            ? demo.Cases[0]
            : demo.Cases.FirstOrDefault(x => string.Equals(x.Template, templateName.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new ValidationFailedException(
                  $"Unknown demo template '{templateName}'. Use one of: {string.Join(", ", demo.Cases.Select(x => x.Template))}.");
        var user = demo.Users.FirstOrDefault(u => u.StudentRef == studentRef)
            ?? throw new ValidationFailedException($"Unknown demo student {studentRef}.");

        var c = await CreateAsync(new CreateCaseRequest(studentRef, user.DisplayName, user.Email, template.Program,
            template.StartDate), actorId, ct);

        var institutions = await db.Institutions.ToDictionaryAsync(i => i.Name, ct);
        await ImportCoursesAsync(c.Id, new ImportCoursesRequest(template.Courses.Select(x => new ImportCourse(
            institutions[x.Institution].Id, x.Code, x.Title, x.Credits, x.Grade, x.Term, x.Description, x.Syllabus)).ToList()),
            actorId, ct);
        return c;
    }
}
