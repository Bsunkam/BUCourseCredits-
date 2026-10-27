# Team Response: Smarter Transfer Credit Evaluation (Problem 1, Boston University)

Our response to the FDE AI Hackathon 5.0 brief, following its "Your task" steps. Copy each section into the matching
part of the Team Response Template. Everything in the prototype uses synthetic data. Sponsor answers are not yet
recorded; where a section depends on them, it says so.

## 1. The problem as we understand it

More than 800 transfer students a year have every earlier course matched, reviewed and posted to their BU record.
Two groups carry the cost:

- **Students** see courses at 0 units with no explanation. They chase old syllabi, and only learn late how much of their
  degree progress is secure.
- **Department evaluators** compare syllabi one by one: course descriptions, learning outcomes and credit hours, again
  and again for the same courses.

The tool must make evaluation faster, more transparent and less stressful, and **the academic department must still
make the final decision**.

## 2. How the work runs today, and where it breaks

| # | Step (from the brief) | Where it breaks | What the prototype does |
| --- | --- | --- | --- |
| 1 | Student self-checks likely equivalencies in Transferology | The preview is not linked to the student's case | Not integrated. See discovery question 9 |
| 2 | Student submits official transcripts | Nothing tells the student what happens next | Portal upload and email intake; the case timeline shows "received" |
| 3 | Credit Evaluation checks each course against the equivalency database | Manual lookup for every course | Automatic lookup in the equivalency table, with no AI call |
| 4 | Existing equivalency: applied, result is a BU course | — | Applied automatically and recorded as "applied from equivalency database". It is held for staff confirmation if the grade can't be checked or the mapping is still marked Check |
| 5 | No match: "maybe"; student must send a syllabus | The student isn't told why; the syllabus is hard to find | Syllabus request with the reason and what to send. The public catalogue description fills the gap meanwhile |
| 6 | Evaluator decides: BU equivalent, elective units, or no credit (0 units) | One-by-one manual comparison | Side-by-side workbench with outcomes and credits, plus an advisory AI ranking with cited evidence. The evaluator decides |
| 7 | Posted to MyBU Student with grade "T"; pending shows 0 units | Student can't tell pending from lost | Simulated posting with grade T and 0 Hub units. The student sees secured, pending and at-risk units separately |

**About the † marks.** The brief says some steps are marked † as the organisers' unconfirmed reading. Our copy of the
document has no visible † marks; they may be in Figures 1–2. So we treat **every** step above as unconfirmed. Each has
a check in section 7 (questions 2–4 and 9–10).

## 3. The problem broken down to its core

**Known (stated in the brief):** more than 800 transfer students a year; pending courses show 0 units; the quarter
conversion is two-thirds (4 becomes 2.66); many schools require a C or higher; external grades don't count toward the
BU GPA; no BU Hub units; a one-year deadline; outcomes posted with grade T; departments decide.

**Assumed (to be tested; see section 7):** most courses are repeats of courses BU has already seen. A syllabus is the
main evidence for a "maybe". Evaluators decide mainly on outcomes, topics and credit hours. Students learn status only
from MyBU Student. Monthly volume and turnaround are not known; the Discovery Log row (45 a month, 18 days) is a
placeholder.

**Why it keeps happening (root causes):**

1. **Decisions are not reused systematically.** A course evaluated once should never need a second manual comparison
   for the next student with identical evidence. The brief's step 4 says the database is checked, but not how complete
   it is or whether every decision is written back.
2. **The syllabus is requested only after the "maybe" result.** The student learns late and has to dig up an old
   document, which is the longest wait in the process.
3. **Status is a single number.** "0 units" means pending, at risk and refused all at once, so students can't tell
   what is secure.
4. **Evidence is assembled by hand.** Each evaluator rebuilds the same comparison: description, outcomes, credits,
   grade and program rules.

## 4. One measurable outcome

**Median time a transfer course sits at zero units: from case opened to the course's final decision.**

- It captures "faster" (time) and "less stressful" (time spent not knowing). It can be measured for every course from
  the case and decision timestamps.
- In the prototype: **Metrics & AI evidence → Primary outcome** (`GET /api/metrics`, `medianHoursAtZeroUnits`).
  It is reported for all courses and separately for courses an evaluator reviewed.
- **Baseline: unknown.** Discovery question 1 asks for it. The target is set with the sponsor once the baseline is
  known; we make no improvement claim before then.

Supporting measures, which are not the outcome: the share of courses resolved without a new review (reuse rate), human
control (every human decision has a named evaluator), AI usage records, and match agreement with past decisions.

## 5. The solution

A reviewer-assist application. Deterministic rules come first, AI comes last, and a human makes every decision.

1. **Rules engine (C#, unit tested):** the quarter conversion, the grade minimum per program, the one-year deadline,
   the BU-only courses (CS 320, 332, 350), grade T and 0 Hub units.
2. **Equivalency lookup first:** a known course gets its existing equivalency applied immediately, with no AI call
   (step 4).
3. **"Maybe" courses:** the student gets a syllabus request with the reason. The public catalogue description and
   outcomes are attached, so evaluators have evidence early.
4. **Agent pipeline (Python, LangGraph):** intake, then policy gate, then matching, then evidence, then a **pause for the
   evaluator**. It ranks BU candidates on description, learning outcomes and credit hours, cites only text found word
   for word in the syllabus, and can never finalise.
5. **Evaluator workbench:** the source and BU courses side by side. The AI's pick is labelled "AI suggestion · verify"
   and must be explicitly confirmed before it can be finalised. Outcomes are BU equivalent, elective units (more than
   0) or no credit.
6. **Write-back:** every final decision becomes an approved equivalency for identical evidence. It is **not** saved
   when the decision depended on this student's grade, so a "no credit" for a D never passes to a student with an A.
7. **Student view:** units secured, pending review and at risk, each with a reason. It also shows the secured units by
   requirement, the deadline, and the posted record with grade T. Advisors see the same summary, read-only.

## 6. The working slice, shown against the data

| Data the brief describes | What the repository holds (all synthetic) |
| --- | --- |
| ~150 BU courses for one program | 155 courses for the CS program and its supporting departments (64 CS) |
| ~200 courses from two other universities | 214 public-catalogue courses (Northeastern 106, NYU 108), plus a community college and a quarter-system college |
| ~100 past decisions | 104 approved equivalencies (79 BU equivalent, 19 elective, 6 no credit) |
| 3–4 transcripts | 2 demo transcripts (6 courses each) and a second-student template |

The data comes from `src/database/scripts/generate_synthetic_data.py` and is deterministic. Codes look like real
catalogues; the descriptions and decisions are invented. Replace it with the organisers' files when they arrive.

**Demo scenario from the brief** (the *New six-course demo case* button; run end to end by
`tests/e2e/six-course-demo.spec.ts`). A community-college student with six courses:

- CSC 110, MAT 210 and PSY 101 have existing equivalencies, applied automatically.
- CS 140 is from a quarter-system school: 4 quarter credits become 2.66 BU credits, applied from its equivalency.
- CSC 215 is a "maybe" with a syllabus: the agent suggests CAS CS 131 and the evaluator decides.
- CSC 230 is a "maybe" without a syllabus: the student sees why one is needed and uploads it, then the evaluator
  awards elective units.
- Staff post the results (simulated). The student sees every course with grade T, 0 Hub units, and all units secured.

A second demo (Northeastern) shows the BU-only block (CS 3800 to CS 332), the grade gate (D in Calculus) and a mapping
still marked Check that is held for staff confirmation.

## 7. How we would prove it worked, and what we don't know yet

**Proof plan:**

1. Agree the baseline for the primary outcome with the sponsor (Q1). Then run the same fixed set of cases through
   today's process and through the prototype, and compare the median time at zero units.
2. Match quality: `python -m eval.run_eval eval/labelled_cases.json` (in `src/agent`) runs the 93 labelled past
   decisions through the agent. Current result in **mock mode** (keyword ranker, not AI):

   | Measure | Result |
   | --- | --- |
   | All cases agree with the evaluator | 84/93 (90%) |
   | BU equivalent expected: top suggestion is that course | 68/68 (100%) |
   | Elective or no credit expected: no BU match suggested | 16/25 (64%) |

   **Read this honestly:** the synthetic source courses are reworded from the BU catalogue, so the 100% is close to
   circular. It shows the pipeline and scoring work, not real accuracy. The 64% is a real weakness: the keyword ranker
   finds false matches, such as Intermediate Algebra against Linear Algebra. Re-run on the sponsor's real past
   decisions with the model enabled before claiming accuracy.
3. Human control: 100% of human decisions carry a named evaluator (Metrics page). Existing equivalencies are counted
   separately, not as human decisions.

**Discovery questions** (asked and answers learned; to be filled in after the sponsor session):

| # | Question | Tests | Answer |
| --- | --- | --- | --- |
| 1 | Monthly course volume, and average time from transcript to posted result, overall and for "maybe" courses? | Baseline for the outcome | *pending* |
| 2 | How does a student send a syllabus today (email, portal, in person), and in what formats? | Steps 2 and 5; intake channels | *pending* |
| 3 | What does a student see in MyBU Student while a course is at 0 units? | Step 7; transparency | *pending* |
| 4 | What do evaluators compare to approve a match? Is there a rubric? | Step 6; the evidence shown | *pending* |
| 5 | May the tool show a suggested outcome, or only the evidence? | AI boundary | *pending* |
| 6 | BU-only courses (CS 320/332/350): block, or warn? | Step 6; the guardrail | *pending* |
| 7 | Are the past decisions representative, and do they record why? | The eval set | *pending* |
| 8 | How are elective units set? How are quarter credits rounded (2.66 or 2.67)? | Steps 6–7; unit counts | *pending* |
| 9 | Does Transferology read from the same equivalency database? Should the tool link to it? | Step 1 | *pending* |
| 10 | Does BU already use any automated or AI course matching? | "What has been tried" | *pending* |
| 11 | Is an existing equivalency applied directly, or does someone confirm it each time? | Step 4; auto-apply | *pending* |

**Assumptions and how to test each:**

| Assumption | How to test |
| --- | --- |
| Most transfer courses repeat courses already evaluated | Share of the sponsor's past decisions per institution and course code that occur more than once |
| An existing equivalency can be applied without a new review | Q11. If not, the hold-for-confirmation path already exists and can be made the default |
| A decision based on a low grade must not be reused | Q8 and Q11 with the sponsor; covered by an automated test |
| Students mostly email syllabi | Q2; intake channel counts once live |
| The public catalogue description is useful evidence before a syllabus | Ask evaluators (Q4) and compare decisions made with and without it |
| Secured units are what advisors and financial aid need | Ask the advisor and financial aid contacts which figure they use |
| Truncation (2.66) is the rounding BU uses | Q8; a single setting (`CreditRounding`) |

**Known unknowns:** the baseline time and volume; the real equivalency database extract; real syllabi; whether a MyBU
Student integration is permitted (posting is simulated); grade thresholds per school; model accuracy on real data;
whether the † steps match how the work really runs.

## 8. Ground rules we followed

- Only synthetic data; no real student, employee or customer data. Demo users are `@demo.local`.
- Departments make every final decision. The agent output requires human review by contract, and any response that
  doesn't is rejected.
- Every assumption is written down above with a test.
