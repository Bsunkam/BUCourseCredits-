import type { CaseStatus, CourseStatus, OutcomeType, ScanStatus } from "./types";

export type Tone = "neutral" | "info" | "warn" | "danger" | "success" | "accent";

// Distinct states (governance rule): nothing AI-suggested may look like an official BU decision.
export const courseStatus: Record<CourseStatus, { label: string; tone: Tone }> = {
  Pending: { label: "Not matched yet", tone: "neutral" },
  Matching: { label: "Matching…", tone: "info" },
  ApprovedMatchFound: { label: "Approved mapping found", tone: "info" },
  NeedsSyllabus: { label: "Syllabus needed", tone: "warn" },
  NeedsReview: { label: "Suggested · needs review", tone: "accent" },
  Blocked: { label: "Blocked by program rule", tone: "danger" },
  Decided: { label: "Final decision", tone: "success" },
};

export const caseStatus: Record<CaseStatus, { label: string; tone: Tone }> = {
  Open: { label: "Open", tone: "neutral" },
  AwaitingDocuments: { label: "Awaiting documents", tone: "warn" },
  InReview: { label: "In review", tone: "accent" },
  Decided: { label: "Decided", tone: "success" },
  Posted: { label: "Posted (simulated)", tone: "success" },
};

export const scanStatus: Record<ScanStatus, { label: string; tone: Tone }> = {
  Pending: { label: "Scanning", tone: "info" },
  Clean: { label: "Clean", tone: "success" },
  Quarantined: { label: "Quarantined", tone: "danger" },
};

export const outcomeLabel: Record<OutcomeType, string> = {
  BuEquivalent: "BU equivalent",
  ElectiveCredit: "Elective credit",
  NoCredit: "No credit (0 units)",
};

export const candidateSource: Record<string, string> = {
  ApprovedRule: "Approved equivalency · no AI call",
  Cache: "Reused suggestion · no AI call",
  Agent: "Agent suggestion",
  Stub: "Stub suggestion (not AI)",
};

export function deadlineText(daysRemaining: number, overdue: boolean): string {
  if (overdue) return `Overdue by ${-daysRemaining} day${daysRemaining === -1 ? "" : "s"}`;
  if (daysRemaining === 0) return "Due today";
  return `${daysRemaining} day${daysRemaining === 1 ? "" : "s"} left`;
}

export function formatDate(iso: string): string {
  const d = new Date(iso.length === 10 ? `${iso}T00:00:00` : iso);
  return d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
