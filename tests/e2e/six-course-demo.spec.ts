import { expect, test, type Page } from "@playwright/test";

async function signInAs(page: Page, userId: string) {
  await page.goto("/");
  await page.getByLabel("Demo user").selectOption(userId);
}

test("six-course demo: rules, review, human decision and student view", async ({ page }) => {
  // Staff creates a fresh synthetic case and runs matching.
  await signInAs(page, "staff-1");
  await page.getByRole("link", { name: "Cases", exact: true }).click(); // staff land on the dashboard
  await page.getByRole("button", { name: /New six-course demo case/ }).click();
  await expect(page.getByRole("heading", { name: /TC-\d+ · Demo Student A/ })).toBeVisible();
  const caseUrl = page.url();
  await page.getByRole("button", { name: /Run matching/ }).click();

  const courses = page.locator("section", { has: page.getByRole("heading", { name: "Courses" }) });
  await expect(courses.getByText("Blocked by program rule")).toBeVisible({ timeout: 90_000 }); // a real model call can take ~30 s
  // 3 seeded mappings; CS 2510 is a 4th when an evaluator already decided it in this environment (reuse).
  expect(await courses.getByText("Approved mapping found").count()).toBeGreaterThanOrEqual(3);
  await expect(page.getByRole("row", { name: /CS 2510/ })).toContainText("CAS CS 112");
  await expect(courses.getByText("Syllabus needed")).toBeVisible();
  await expect(courses.getByText("2.66")).toBeVisible();
  // Nothing is final before a human decides.
  await expect(courses.getByText("Final decision")).toHaveCount(0);

  // The evaluator reviews the "maybe" course and records the decision.
  await signInAs(page, "eval-cs");
  await page.goto(caseUrl);
  await page.getByRole("row", { name: /CS 2510/ }).getByRole("button", { name: "Review" }).click();
  await expect(page.getByRole("heading", { name: /Evaluator workbench · CS 2510/ })).toBeVisible();
  await page.getByLabel(/Rationale/).fill("Syllabus covers object-oriented Java and core data structures.");
  await page.getByRole("button", { name: "Finalise decision" }).click();
  await expect(page.getByRole("heading", { name: "Final decision" })).toBeVisible();

  // The student sees status, next action and decided units, without reviewer notes.
  await signInAs(page, "student-a");
  await expect(page.getByRole("heading", { name: "My transfer credit" })).toBeVisible();
  await expect(page.getByText(/Send the syllabus for: .*CS 140/).first()).toBeVisible();
  await expect(page.getByText("Syllabus covers object-oriented Java")).toHaveCount(0);
});
