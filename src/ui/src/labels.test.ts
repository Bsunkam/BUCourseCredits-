import { describe, expect, it } from "vitest";
import { courseStatus, deadlineText, formatBytes } from "./labels";

describe("labels", () => {
  it("never labels an AI suggestion as a final decision", () => {
    expect(courseStatus.NeedsReview.label).toMatch(/needs review/i);
    expect(courseStatus.NeedsReview.tone).not.toBe("success");
    expect(courseStatus.Decided.tone).toBe("success");
  });

  it("describes deadlines", () => {
    expect(deadlineText(10, false)).toBe("10 days left");
    expect(deadlineText(1, false)).toBe("1 day left");
    expect(deadlineText(-3, true)).toBe("Overdue by 3 days");
    expect(deadlineText(0, false)).toBe("Due today");
  });

  it("formats sizes", () => {
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
  });
});
