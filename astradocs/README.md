# AstraDocs: get comfortable in the Atlas codebase

This is a patient onboarding guide for a junior engineer. You can read a short explanation, see the same idea as a picture, follow it in real code, and try it yourself. Repetition is intentional. There is no deadline for understanding a page.

You do not need to read the whole repository first. Start with one request and a few files. This folder helps you become comfortable navigating Atlas; the existing [learning curriculum](../docs/learning/README.md) continues into broader engineering skills.

When you are ready to practice, choose one of the [25 junior user stories](16-junior-user-stories.md).
Each has detailed acceptance criteria, exact source files, small implementation steps,
test data, verification commands, and a completion checklist. The bootcamp below
now tracks their implementation and provides exercises for practicing the changes.

For larger features, use the [30 mid-level feature stories](17-midlevel-feature-stories.md).
These connect multiple parts of Atlas through dashboards, payment tracking, workflow
changes, previews, and API key management. Each includes a source reading path,
acceptance criteria, numbered implementation steps, concrete test cases, and a
teach-back question. The workbook also explains dependencies, database setup, and
concurrent writes. Use the bootcamp register for each story's implementation status.

Continue with the [20 mid/senior feature stories](18-mid-senior-feature-stories.md)
for payroll simulations and jobs, billing schedules, cost allocations, staged
imports, audit records, exports, and approval policies. Each provides detailed
implementation steps, source reading paths, acceptance criteria, failure tests,
and explanations to practice. Dependencies are explicit; verified behavior is
recorded in the bootcamp rather than inferred from a story's acceptance criteria.

## Choose a starting route

The [implementation bootcamp](bootcamp/README.md) now tracks building all 75 stories,
with an engineering journal, teaching lessons, and a per-story progress register.
Use that register to distinguish planned work from implemented and verified behavior.

| What would help right now? | Start here | Then revisit it this way |
| --- | --- | --- |
| “Tell me what this app does.” | [The story](01-the-story.md) | [Pictures and maps](02-pictures-and-maps.md) |
| “Help me get it running.” | [First 20 minutes](00-first-20-minutes.md) | [Follow a read request](04-follow-a-read.md) |
| “There are too many files.” | [A small file tour](03-file-tour.md) | [Where would I change this?](11-first-small-change.md) |
| “I understand the story, but not the C#.” | [Read code slowly](06-read-code-slowly.md) | [Words and symbols](14-words-and-symbols.md) |
| “I keep mixing up objects and the database.” | [Memory versus storage](07-memory-and-storage.md) | [Follow a write](05-follow-a-write.md) |
| “I understand it when reading, then forget.” | [Practice and recall](12-practice-and-recall.md) | [Answers with explanations](13-answers.md) |
| “I am onboarding someone else.” | [Pairing and mentor guide](15-pairing-guide.md) | [Practice and recall](12-practice-and-recall.md) |

## The five questions we will keep repeating

1. **Who is asking?** The caller and their permissions.
2. **Where does it go?** The route and its handler.
3. **What gets checked?** Input, ownership, and business rules.
4. **What changes or gets read?** Objects in memory and database records.
5. **What comes back?** HTTP status, response body, and headers.

These questions work for a tiny country lookup and a much larger payroll operation. You do not have to memorize an entire file to answer them.

## Read in small passes

On the first pass, read the opening explanation and the “Say it again” part. On the second, open the linked source file. On the third, answer a checkpoint without looking. If one part is unclear, return to that part; you do not need to restart everything.

Suggested sessions, adjustable to your pace:

| Session | One objective | Pages |
| --- | --- | --- |
| A | Run Atlas and describe its purpose | [00](00-first-20-minutes.md), [01](01-the-story.md) |
| B | Find one handler and one database model | [02](02-pictures-and-maps.md), [03](03-file-tour.md) |
| C | Explain a successful read | [04](04-follow-a-read.md), [06](06-read-code-slowly.md) |
| D | Explain why a write can be rejected | [05](05-follow-a-write.md), [08](08-access-and-errors.md) |
| E | Separate temporary state from stored state | [07](07-memory-and-storage.md), [09](09-payroll-with-small-numbers.md) |
| F | Read a test and propose a small change | [10](10-tests-and-debugging.md), [11](11-first-small-change.md) |
| Repeat | Recall one idea in a new form | [12](12-practice-and-recall.md), [13](13-answers.md) |

Keep a bookmark: “I was reading __. I understand __. I am unsure about __.” That is enough to resume after a break.
