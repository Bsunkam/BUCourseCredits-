using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferCredit.Api.Contracts;
using TransferCredit.Data;
using TransferCredit.Data.Entities;

namespace TransferCredit.Api.Tests;

/// <summary>Runs the API in memory with the EF in-memory provider, local storage and the stub agent.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _db = $"tc-{Guid.NewGuid():N}";
    private readonly string _storage = Path.Combine(Path.GetTempPath(), $"tc-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=unused");
        builder.UseSetting("Storage:LocalRoot", _storage);
        builder.UseSetting("Storage:ScanMode", "Simulated");
        builder.UseSetting("AGENT_BASE_URL", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TransferCreditDbContext>>();
            services.AddDbContext<TransferCreditDbContext>(o => o.UseInMemoryDatabase(_db));
        });
    }

    public HttpClient As(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User", userId);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_storage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList()) services.Remove(d);
    }
}

/// <summary>Each test gets its own app and database: approved decisions are shared state by design.</summary>
public sealed class WorkflowTests : IDisposable
{
    private readonly ApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<CaseDetailDto> WaitForMatchingAsync(HttpClient staff, int caseId)
    {
        for (var i = 0; i < 100; i++)
        {
            var detail = (await staff.GetFromJsonAsync<CaseDetailDto>($"/api/cases/{caseId}", Json))!;
            if (detail.Courses.All(c => c.Status != CourseStatus.Matching)) return detail;
            await Task.Delay(100);
        }
        throw new TimeoutException("Matching did not finish.");
    }

    internal const string Northeastern = "northeastern";

    private static async Task<int> CreateDemoCaseAsync(HttpClient staff, string studentRef, string? template = null)
    {
        var response = await staff.PostAsJsonAsync("/api/demo/cases", new { studentRef, template });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<CaseDetailDto> MatchAsync(HttpClient staff, int caseId)
    {
        (await staff.PostAsync($"/api/cases/{caseId}/match", null)).EnsureSuccessStatusCode();
        return await WaitForMatchingAsync(staff, caseId);
    }

    private static async Task<EvaluationDto> DecideAsync(
        HttpClient client, int courseId, OutcomeType outcome, int? buCourseId, string rationale, decimal? units = null)
    {
        var draft = await client.PostAsJsonAsync("/api/evaluations",
            new CreateEvaluationRequest(courseId, outcome, buCourseId, units, rationale), Json);
        draft.EnsureSuccessStatusCode();
        var id = (await draft.Content.ReadFromJsonAsync<EvaluationDto>(Json))!.Id;
        var final = await client.PostAsync($"/api/evaluations/{id}/finalize", null);
        final.EnsureSuccessStatusCode();
        return (await final.Content.ReadFromJsonAsync<EvaluationDto>(Json))!;
    }

    [Fact]
    public async Task Six_course_demo_follows_the_brief()
    {
        var staff = factory.As("staff-1");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001");
        var byCode = (await MatchAsync(staff, caseId)).Courses.ToDictionary(c => c.Code);

        // 3 existing equivalencies are applied straight away (step 4), with no AI call and no new review.
        Assert.All(new[] { "CSC 110", "MAT 210", "PSY 101" }, code =>
        {
            Assert.Equal(CourseStatus.Decided, byCode[code].Status);
            Assert.Equal("system:equivalency-db", byCode[code].Evaluation!.EvaluatorId);
            Assert.Equal(byCode[code].ConvertedCredits, byCode[code].Evaluation!.AwardedUnits);
        });
        // Quarter-system course: 4 quarter credits become 2.66 BU credits (BR-03), applied from its equivalency.
        Assert.Equal(2.66m, byCode["CS 140"].ConvertedCredits);
        Assert.Equal(CourseStatus.Decided, byCode["CS 140"].Status);
        Assert.Equal(2.66m, byCode["CS 140"].Evaluation!.AwardedUnits);
        // 2 "maybe" courses: one has a syllabus and goes to review; the other needs one (BR-08).
        Assert.Equal(CourseStatus.NeedsReview, byCode["CSC 215"].Status);
        Assert.Equal("CAS CS 131", byCode["CSC 215"].TopCandidate!.BuCourse.Code);
        Assert.Equal(CourseStatus.NeedsSyllabus, byCode["CSC 230"].Status);
        // The public catalogue gives the evaluator a description and outcomes before the syllabus arrives.
        Assert.False(string.IsNullOrWhiteSpace(byCode["CSC 230"].Description));
        Assert.False(string.IsNullOrWhiteSpace(byCode["CSC 230"].LearningOutcomes));

        // Applied equivalencies are reported apart from human decisions, and feed the primary outcome measure.
        var metrics = (await staff.GetFromJsonAsync<MetricsDto>("/api/metrics", Json))!;
        Assert.Equal(4, metrics.AppliedFromEquivalencyDb);
        Assert.Equal(0, metrics.FinalDecisions);
        Assert.NotNull(metrics.MedianHoursAtZeroUnits);
        Assert.Null(metrics.MedianHoursAtZeroUnitsReviewed);
        Assert.Equal(2 + 6, metrics.CoursesStillAtZeroUnits); // this case's 2 "maybe" + the start-up demo case's 6
    }

    [Fact]
    public async Task Six_course_demo_runs_from_syllabus_to_posting_with_grade_T()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");
        var student = factory.As("student-a");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001");
        var byCode = (await MatchAsync(staff, caseId)).Courses.ToDictionary(c => c.Code);

        var before = (await student.GetFromJsonAsync<StudentStatusDto>($"/api/cases/{caseId}/status", Json))!;
        var waiting = before.Courses.Single(c => c.Code == "CSC 230");
        Assert.Equal("At risk", waiting.Standing);
        Assert.Contains("syllabus", waiting.SyllabusReason!);
        Assert.Equal(0m, waiting.AwardedUnits); // BR-02
        Assert.Equal(3m, waiting.PotentialUnits);
        Assert.Equal(1, before.Progress.CoursesNeedingAction);
        Assert.Equal(3m + 4m + 3m + 2.66m, before.Progress.SecuredUnits);

        // The syllabus arrives; the course is matched again and the evaluator decides both "maybe" courses.
        (await staff.PutAsJsonAsync($"/api/courses/{byCode["CSC 230"].Id}/syllabus",
            new UpdateSyllabusRequest("Outcomes: write C programs using pointers and dynamic memory; explain binary data representation, " +
                                      "memory and caches; read assembly language; describe how processes and linking work."))).EnsureSuccessStatusCode();
        byCode = (await MatchAsync(staff, caseId)).Courses.ToDictionary(c => c.Code);
        Assert.Equal(CourseStatus.NeedsReview, byCode["CSC 230"].Status);

        await DecideAsync(evaluator, byCode["CSC 215"].Id, OutcomeType.BuEquivalent, byCode["CSC 215"].TopCandidate!.BuCourse.Id,
            "Logic, proofs, counting and graphs match CS 131.");
        await DecideAsync(evaluator, byCode["CSC 230"].Id, OutcomeType.ElectiveCredit, null,
            "Covers part of CS 210; award elective units.");

        (await staff.PostAsync($"/api/cases/{caseId}/post", null)).EnsureSuccessStatusCode();
        var after = (await student.GetFromJsonAsync<StudentStatusDto>($"/api/cases/{caseId}/status", Json))!;

        Assert.Equal(CaseStatus.Posted, after.Status);
        Assert.All(after.Courses, c =>
        {
            Assert.Equal("T", c.PostedGrade);     // step 7: posted with a grade of T
            Assert.Equal(0m, c.HubUnits);         // BR-05
            Assert.Equal("Secured", c.Standing);
        });
        Assert.Equal(after.Courses.Sum(c => c.AwardedUnits), after.Progress.SecuredUnits);
        Assert.Equal(0m, after.Progress.PendingUnits + after.Progress.AtRiskUnits);
        Assert.Contains(after.Progress.SecuredByRequirement, r => r.Requirement == "General elective units");
        Assert.Contains(after.PolicyNotes, n => n.Contains("BU GPA"));
        Assert.Contains(after.PolicyNotes, n => n.Contains("Hub"));
    }

    [Fact]
    public async Task Northeastern_demo_shows_bu_only_block_grade_gate_and_unverified_mapping()
    {
        var staff = factory.As("staff-1");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001", Northeastern);
        var byCode = (await MatchAsync(staff, caseId)).Courses.ToDictionary(c => c.Code);

        Assert.Equal(CourseStatus.Decided, byCode["CS 2500"].Status);
        // A mapping still marked Check is held for staff confirmation instead of being applied (BR-12).
        Assert.Equal(CourseStatus.ApprovedMatchFound, byCode["PSYC 1101"].Status);
        Assert.Equal(CourseStatus.NeedsReview, byCode["CS 2510"].Status);
        Assert.Equal("CAS CS 112", byCode["CS 2510"].TopCandidate!.BuCourse.Code);
        Assert.Equal(CourseStatus.Blocked, byCode["CS 3800"].Status); // BR-10
        Assert.True(byCode["CS 3800"].TopCandidate!.IsBlocked);
        Assert.Equal(CourseStatus.NeedsReview, byCode["MATH 3A"].Status); // grade D: policy gate (BR-04)
        Assert.Null(byCode["MATH 3A"].TopCandidate);
    }

    [Fact]
    public async Task Only_evaluators_finalise_and_bu_only_courses_cannot_be_awarded()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001", Northeastern);
        var detail = await MatchAsync(staff, caseId);
        var cs2510 = detail.Courses.Single(c => c.Code == "CS 2510");
        var cs3800 = detail.Courses.Single(c => c.Code == "CS 3800");

        var draft = await staff.PostAsJsonAsync("/api/evaluations", new CreateEvaluationRequest(
            cs2510.Id, OutcomeType.BuEquivalent, cs2510.TopCandidate!.BuCourse.Id, null, "Looks equivalent"), Json);
        var draftId = (await draft.Content.ReadFromJsonAsync<EvaluationDto>(Json))!.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/evaluations/{draftId}/finalize", null)).StatusCode);

        var blocked = await evaluator.PostAsJsonAsync("/api/evaluations", new CreateEvaluationRequest(
            cs3800.Id, OutcomeType.BuEquivalent, cs3800.TopCandidate!.BuCourse.Id, null, "Try"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var noRationale = await evaluator.PostAsJsonAsync("/api/evaluations", new CreateEvaluationRequest(
            cs2510.Id, OutcomeType.ElectiveCredit, null, null, " "), Json);
        Assert.Equal(HttpStatusCode.BadRequest, noRationale.StatusCode);

        var final = await evaluator.PostAsync($"/api/evaluations/{draftId}/finalize", null);
        final.EnsureSuccessStatusCode();
        var evaluation = (await final.Content.ReadFromJsonAsync<EvaluationDto>(Json))!;
        Assert.Equal(EvaluationStatus.Final, evaluation.Status);
        Assert.Equal("eval-cs", evaluation.EvaluatorId);
        Assert.Equal(4m, evaluation.AwardedUnits);
    }

    [Fact]
    public async Task Second_student_reuses_approved_decision_without_an_agent_call()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");

        var first = await CreateDemoCaseAsync(staff, "DS-0001");
        var csc215 = (await MatchAsync(staff, first)).Courses.Single(c => c.Code == "CSC 215");
        await DecideAsync(evaluator, csc215.Id, OutcomeType.BuEquivalent, csc215.TopCandidate!.BuCourse.Id, "Outcomes match CS 131");

        var second = await CreateDemoCaseAsync(staff, "DS-0002");
        var reused = (await MatchAsync(staff, second)).Courses.Single(c => c.Code == "CSC 215");

        // The saved decision is an approved equivalency now, so it is applied like any other (step 4).
        Assert.Equal(CourseStatus.Decided, reused.Status);
        Assert.True(reused.AppliedRule!.FromEvaluation);

        var course = (await staff.GetFromJsonAsync<CourseDetailDto>($"/api/courses/{reused.Id}", Json))!;
        Assert.All(course.AgentRuns, r => Assert.NotEqual(AgentRunSource.NewRun, r.Source));
    }

    [Fact]
    public async Task No_credit_given_for_a_low_grade_is_not_reused_for_a_student_with_a_good_grade()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");

        // Student A has a D in MATH 3A; the evaluator gives no credit because of the grade.
        var first = await CreateDemoCaseAsync(staff, "DS-0001", Northeastern);
        var low = (await MatchAsync(staff, first)).Courses.Single(c => c.Code == "MATH 3A");
        await DecideAsync(evaluator, low.Id, OutcomeType.NoCredit, null, "Grade D is below the minimum C.");

        // Student B took the same course and got an A.
        var second = await CreateDemoCaseAsync(staff, "DS-0002");
        var institutions = (await staff.GetFromJsonAsync<List<InstitutionDto>>("/api/institutions", Json))!;
        var pcc = institutions.Single(i => i.Name == "Pacific Coast College (synthetic)");
        (await staff.PostAsJsonAsync($"/api/cases/{second}/courses/import", new ImportCoursesRequest(
            [new ImportCourse(pcc.Id, "MATH 3A", "Calculus I", 4, "A", "Winter 2024", null, null)]), Json)).EnsureSuccessStatusCode();
        var good = (await MatchAsync(staff, second)).Courses.Single(c => c.Code == "MATH 3A");

        Assert.NotEqual(CourseStatus.Decided, good.Status);
        Assert.Null(good.AppliedRule);
    }

    [Fact]
    public async Task Elective_credit_must_award_units_and_staff_cannot_change_units_when_confirming()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001", Northeastern);
        var byCode = (await MatchAsync(staff, caseId)).Courses.ToDictionary(c => c.Code);

        var zeroElective = await evaluator.PostAsJsonAsync("/api/evaluations", new CreateEvaluationRequest(
            byCode["CS 2510"].Id, OutcomeType.ElectiveCredit, null, 0m, "Elective"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, zeroElective.StatusCode); // BR-09: elective credit counts as units

        // PSYC 1101 is held for confirmation (mapping marked Check). Staff may confirm it only as it stands.
        var psyc = byCode["PSYC 1101"];
        Assert.Equal(CourseStatus.ApprovedMatchFound, psyc.Status);
        var buId = psyc.AppliedRule!.BuCourse!.Id;
        var changed = await staff.PostAsJsonAsync("/api/evaluations",
            new CreateEvaluationRequest(psyc.Id, OutcomeType.BuEquivalent, buId, 2m, "Confirm with fewer units"), Json);
        var changedId = (await changed.Content.ReadFromJsonAsync<EvaluationDto>(Json))!.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/evaluations/{changedId}/finalize", null)).StatusCode);

        var confirmed = await DecideAsync(staff, psyc.Id, OutcomeType.BuEquivalent, buId, "Confirmed existing mapping");
        Assert.Equal(psyc.ConvertedCredits, confirmed.AwardedUnits);
    }

    [Fact]
    public async Task Students_see_only_their_own_case_and_no_reviewer_notes()
    {
        var staff = factory.As("staff-1");
        var studentA = factory.As("student-a");
        var aCase = await CreateDemoCaseAsync(staff, "DS-0001");
        var bCase = await CreateDemoCaseAsync(staff, "DS-0002");

        Assert.Equal(HttpStatusCode.OK, (await studentA.GetAsync($"/api/cases/{aCase}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentA.GetAsync($"/api/cases/{bCase}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentA.GetAsync($"/api/cases/{aCase}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await studentA.GetAsync($"/api/courses/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/cases")).StatusCode);

        var status = (await studentA.GetFromJsonAsync<StudentStatusDto>($"/api/cases/{aCase}/status", Json))!;
        Assert.Equal(0m, status.PendingUnitsShown);
        Assert.All(status.Courses, c => Assert.Equal(0m, c.AwardedUnits)); // BR-02
    }

    [Fact]
    public async Task Uploads_are_validated_deduplicated_and_scanned()
    {
        var staff = factory.As("staff-1");
        var student = factory.As("student-a");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001");

        async Task<HttpResponseMessage> Upload(HttpClient client, string name, byte[] bytes)
        {
            var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(bytes), "file", name },
                { new StringContent("Syllabus"), "documentType" }
            };
            return await client.PostAsync($"/api/cases/{caseId}/documents", form);
        }

        var pdf = "%PDF-1.4 synthetic syllabus"u8.ToArray();
        Assert.Equal(HttpStatusCode.OK, (await Upload(student, "syllabus.pdf", pdf)).StatusCode);
        var again = await (await Upload(student, "copy.pdf", pdf)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(again.GetProperty("duplicate").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(student, "virus.exe", "MZ"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(student, "fake.pdf", "PK\u0003\u0004"u8.ToArray())).StatusCode);

        var eicar = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.4 X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        Assert.Equal(HttpStatusCode.OK, (await Upload(staff, "eicar.pdf", eicar)).StatusCode);

        CaseDetailDto detail;
        var tries = 0;
        do
        {
            await Task.Delay(500);
            detail = (await staff.GetFromJsonAsync<CaseDetailDto>($"/api/cases/{caseId}", Json))!;
        } while (detail.Documents.Any(d => d.ScanStatus == ScanStatus.Pending) && ++tries < 30);

        Assert.Equal(ScanStatus.Clean, detail.Documents.Single(d => d.FileName == "syllabus.pdf").ScanStatus);
        Assert.Equal(ScanStatus.Quarantined, detail.Documents.Single(d => d.FileName == "eicar.pdf").ScanStatus);
        Assert.Equal(2, detail.Documents.Count);
    }

    [Fact]
    public async Task Email_without_case_reference_goes_to_triage_and_is_processed_once()
    {
        var staff = factory.As("staff-1");
        var b64 = Convert.ToBase64String("%PDF-1.4 emailed syllabus"u8.ToArray());
        var eml = $"From: unknown@else.example\r\nSubject: syllabus\r\nMessage-ID: <{Guid.NewGuid():N}@test>\r\n" +
                  "MIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"B\"\r\n\r\n" +
                  "--B\r\nContent-Type: text/plain\r\n\r\nbody text is never stored\r\n" +
                  "--B\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename=\"syllabus.pdf\"\r\n" +
                  $"Content-Transfer-Encoding: base64\r\n\r\n{b64}\r\n--B--\r\n";

        async Task<JsonElement> Import()
        {
            var form = new MultipartFormDataContent { { new StringContent(eml), "file", "mail.eml" } };
            var response = await staff.PostAsync("/api/intake/import-eml", form);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        var first = await Import();
        Assert.Equal("Unmatched", first.GetProperty("matchStatus").GetString());
        Assert.Equal(1, first.GetProperty("accepted").GetInt32());
        Assert.True((await Import()).GetProperty("duplicate").GetBoolean());
    }
}

