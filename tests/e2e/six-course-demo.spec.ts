import { expect, test, type Page } from "@playwright/test";

// The seeded demo accounts share Auth:SeedPassword (appsettings.Development.json).
const PASSWORD = process.env.E2E_PASSWORD ?? "TransferDemo#2026";

async function signInAs(page: Page, userId: string) {
  await page.goto("/");
  const signOut = page.getByRole("button", { name: "Sign out" });
  if (await signOut.isVisible()) {
    await signOut.click();
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  }
  await page.goto("/login");
  await page.getByLabel("Username or email").fill(userId);
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
}

const coursesSection = (page: Page) => page.locator("section", { has: page.getByRole("heading", { name: "Courses" }) });

async function openCourse(page: Page, caseUrl: string, code: string) {
  await page.goto(caseUrl);
  await page.getByRole("row", { name: new RegExp(code) }).getByRole("button", { name: "Review" }).click();
  await expect(page.getByRole("heading", { name: new RegExp(`Evaluator workbench . ${code}`) })).toBeVisible();
}

// The brief's suggested scenario: a community-college transfer with six courses. Three have existing equivalencies,
// two are "maybe", one is from a quarter-system school. Followed from upload to final posting.
test("six-course demo: from upload to final posting", async ({ page }) => {
  // ---- Staff creates the synthetic case and runs matching ----
  await signInAs(page, "staff-1");
  await page.getByRole("link", { name: "Cases", exact: true }).click(); // staff land on the dashboard
  await page.getByRole("button", { name: /New six-course demo case/ }).click();
  await expect(page.getByRole("heading", { name: /TC-\d+ . Demo Student A/ })).toBeVisible();
  const caseUrl = page.url();
  await page.getByRole("button", { name: /Run matching/ }).click();

  const courses = coursesSection(page);
  await expect(courses.getByText("Syllabus needed")).toBeVisible({ timeout: 90_000 }); // a real model call can take ~30 s
  // 3 existing equivalencies plus the quarter-system course are applied from the equivalency database (step 4).
  expect(await courses.getByText("Applied from equivalency database").count()).toBeGreaterThanOrEqual(4);
  await expect(page.getByRole("row", { name: /CS 140/ })).toContainText("2.66"); // 4 quarter credits -> 2.66 (BR-03)
  // The "maybe" course with a syllabus is a suggestion only. In an environment where an evaluator already decided it,
  // that decision was saved as an approved equivalency and is applied instead (reuse, BR-14).
  const csc215Reused = (await page.getByRole("row", { name: /CSC 215/ }).innerText()).includes("Final decision");
  if (!csc215Reused) await expect(page.getByRole("row", { name: /CSC 215/ })).toContainText("Suggested · needs review");

  // ---- The student sees why a syllabus is needed and uploads it, linked to the course ----
  await signInAs(page, "student-a");
  await expect(page.getByRole("heading", { name: "My transfer credit" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Syllabus requests/ })).toBeVisible();
  await expect(page.getByText(/BU has no existing equivalency for CSC 230/)).toBeVisible();
  await expect(page.getByText("Units at risk")).toBeVisible();
  await page.getByLabel("Document type").selectOption("Syllabus");
  await page.getByLabel("For course").selectOption({ label: "CSC 230" });
  await page.getByLabel("Choose files").setInputFiles({
    name: "csc230-syllabus.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4 synthetic CSC 230 syllabus"),
  });
  // Identical bytes from an earlier run are recognised by checksum and not stored again (BR-15).
  await expect(page.getByText(/csc230-syllabus.pdf: (received, scanning|already received, not stored again)/)).toBeVisible();

  // ---- Staff records the syllabus text and re-runs matching ----
  await signInAs(page, "staff-1");
  await openCourse(page, caseUrl, "CSC 230");
  await page.getByRole("button", { name: /Add syllabus text|Edit/ }).click();
  await page.locator("textarea").first().fill(
    "Outcomes: write C programs using pointers and dynamic memory; explain binary data representation, memory and caches; " +
    "read assembly language; describe how processes and linking work.");
  await page.getByRole("button", { name: "Save and mark for re-matching" }).click();
  await page.getByRole("button", { name: "Re-run matching" }).click();
  // Either the agent suggests a match, or an earlier evaluator decision on identical evidence is applied (reuse).
  const suggested = page.getByText("Suggested · needs review").first();
  const decided = page.getByRole("heading", { name: "Final decision" });
  await expect(suggested.or(decided)).toBeVisible({ timeout: 90_000 });
  const csc230Reused = await decided.isVisible();

  // ---- The evaluator decides the "maybe" courses that were not already resolved ----
  await signInAs(page, "eval-cs");
  if (!csc215Reused) {
    await openCourse(page, caseUrl, "CSC 215");
    await expect(page.getByText("AI suggestion · verify")).toBeVisible(); // the agent's pick is not a decision
    await page.getByLabel(/Rationale/).fill("Logic, proofs, counting and graphs match the BU course.");
    await expect(page.getByRole("button", { name: "Finalise decision" })).toBeDisabled();
    await page.getByLabel(/I compared the evidence and choose/).check();
    await page.getByRole("button", { name: "Finalise decision" }).click();
    await expect(page.getByRole("heading", { name: "Final decision" })).toBeVisible();
  }

  if (!csc230Reused) {
    await openCourse(page, caseUrl, "CSC 230");
    await page.getByLabel("Elective credit").check();
    await page.getByLabel(/Rationale/).fill("Covers part of CS 210; award elective units.");
    await page.getByRole("button", { name: "Finalise decision" }).click();
    await expect(page.getByRole("heading", { name: "Final decision" })).toBeVisible();
  }

  // ---- Staff posts the results (simulated) ----
  await signInAs(page, "staff-1");
  await page.goto(caseUrl);
  await page.getByRole("button", { name: "Post results (simulated)" }).click();
  await expect(page.getByText("Posted (simulated)").first()).toBeVisible({ timeout: 30_000 });
  await expect(coursesSection(page).getByText("Grade T").first()).toBeVisible();

  // ---- The student sees the posted record: grade T, 0 Hub units, all units secured, no reviewer notes ----
  await signInAs(page, "student-a");
  const record = page.locator(".card", { has: page.getByRole("heading", { name: /Posted transfer record/ }) });
  await expect(record).toBeVisible();
  expect(await record.getByRole("row").count()).toBe(7); // header + six posted courses
  await expect(record.getByRole("cell", { name: "T", exact: true }).first()).toBeVisible();
  await expect(page.getByText(/do not count toward your BU GPA/).first()).toBeVisible();
  await expect(page.getByText(/do not earn BU Hub/).first()).toBeVisible();
  await expect(page.getByText("Logic, proofs, counting and graphs")).toHaveCount(0);
});
