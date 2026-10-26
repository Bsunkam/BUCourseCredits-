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

    private static async Task<int> CreateDemoCaseAsync(HttpClient staff, string studentRef)
    {
        var response = await staff.PostAsJsonAsync("/api/demo/cases", new { studentRef });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Six_course_demo_follows_the_rulebook()
    {
        var staff = factory.As("staff-1");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001");

        (await staff.PostAsync($"/api/cases/{caseId}/match", null)).EnsureSuccessStatusCode();
        var detail = await WaitForMatchingAsync(staff, caseId);
        var byCode = detail.Courses.ToDictionary(c => c.Code);

        // 3 known matches from approved equivalencies, with no agent call.
        Assert.All(new[] { "CS 2500", "MATH 1341", "PSYC 1101" },
            code => Assert.Equal(CourseStatus.ApprovedMatchFound, byCode[code].Status));
        // 2 "maybe" courses with syllabi go to review; the BU-only candidate is blocked (BR-10).
        Assert.Equal(CourseStatus.NeedsReview, byCode["CS 2510"].Status);
        Assert.Equal("CAS CS 112", byCode["CS 2510"].TopCandidate!.BuCourse.Code);
        Assert.Equal(CourseStatus.Blocked, byCode["CS 3800"].Status);
        Assert.True(byCode["CS 3800"].TopCandidate!.IsBlocked);
        // Quarter course: 4 -> 2.66 and no syllabus means "maybe" plus a syllabus request (BR-03, BR-08).
        Assert.Equal(2.66m, byCode["CS 140"].ConvertedCredits);
        Assert.Equal(CourseStatus.NeedsSyllabus, byCode["CS 140"].Status);
    }

    [Fact]
    public async Task Only_evaluators_finalise_and_bu_only_courses_cannot_be_awarded()
    {
        var staff = factory.As("staff-1");
        var evaluator = factory.As("eval-cs");
        var caseId = await CreateDemoCaseAsync(staff, "DS-0001");
        await staff.PostAsync($"/api/cases/{caseId}/match", null);
        var detail = await WaitForMatchingAsync(staff, caseId);
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
        await staff.PostAsync($"/api/cases/{first}/match", null);
        var cs2510 = (await WaitForMatchingAsync(staff, first)).Courses.Single(c => c.Code == "CS 2510");
        var draft = await evaluator.PostAsJsonAsync("/api/evaluations", new CreateEvaluationRequest(
            cs2510.Id, OutcomeType.BuEquivalent, cs2510.TopCandidate!.BuCourse.Id, null, "Outcomes match CS 112"), Json);
        var draftId = (await draft.Content.ReadFromJsonAsync<EvaluationDto>(Json))!.Id;
        (await evaluator.PostAsync($"/api/evaluations/{draftId}/finalize", null)).EnsureSuccessStatusCode();

        var second = await CreateDemoCaseAsync(staff, "DS-0002");
        await staff.PostAsync($"/api/cases/{second}/match", null);
        var reused = (await WaitForMatchingAsync(staff, second)).Courses.Single(c => c.Code == "CS 2510");

        Assert.Equal(CourseStatus.ApprovedMatchFound, reused.Status);
        Assert.True(reused.AppliedRule!.FromEvaluation);

        var course = (await staff.GetFromJsonAsync<CourseDetailDto>($"/api/courses/{reused.Id}", Json))!;
        Assert.All(course.AgentRuns, r => Assert.NotEqual(AgentRunSource.NewRun, r.Source));
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

