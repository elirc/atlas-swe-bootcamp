# A repeatable study routine

The aim is to make engineering decisions easier to explain and check. Finishing a certain number of stories does not automatically assign a job level. Use the evidence below to see what you can do independently and what still needs practice.

## Use one small feature per session

Start with 15–25 minutes. Stop at a clear checkpoint instead of trying to read the whole codebase.

1. Pick one story in [progress](progress.md). Run `python scripts/bootcamp.py show MS-04`, replacing the ID with your choice.
2. Read its user promise and one acceptance test. Predict the response and the saved database state.
3. Trace one request through the source files named in the lesson. Write down the exact rule that decides the result.
4. Run the focused test with `python scripts/bootcamp.py test MS-04`. If it fails, record the actual failure before guessing a cause.
5. Explain the behavior in three sentences without technical terms. Then explain it again using the method and field names.
6. Close the source and answer two recall questions from `python scripts/bootcamp.py quiz`. Check answers afterward with `--answers`.

If the trace becomes confusing, reduce it: one endpoint, one caller role, one record, one successful request. Add an error case only after that path makes sense.

## Repeat with a different representation

| First pass | Next pass | What repetition should reveal |
| --- | --- | --- |
| Read a request and response | Draw its data path | Which layer owns each decision |
| Read a version check | Draw two competing requests | What can change after an early check |
| Calculate an invoice balance | Read its ledger query | Which facts and dates enter the sum |
| Read a test failure | Inspect entity states in a debugger | Why the implementation behaved differently |
| Explain a feature aloud | Write a short PR description | Whether the promise is clear without code |

Repeat the same idea with a new example, not only the same words. If 100 minus 40 is familiar, try 132 minus 40 minus 12. If one role is familiar, switch from a platform administrator to a client viewer and predict the permission result.

## Return later

Revisit a concept the next day, a few days later, and the following week. These are optional reminders, not deadlines. Before reopening the lesson, try to reconstruct the request flow or arithmetic from memory. A gap tells you what to reread; it is useful feedback.

Keep a personal copy of the [study journal template](study-journal-template.md). Record questions in your own words. Avoid copying API keys, personal worker data, or large logs into notes; IDs from the synthetic test fixture are enough to explain these exercises.

## Observable progression

| Capability | An initial step | A stronger demonstration |
| --- | --- | --- |
| Reading code | Find the endpoint from its route | Trace ownership, validation, persistence, and response without searching every file |
| Making a change | Change one field bound | Preserve callers, add the meaningful edge case, and explain compatibility |
| Testing | Check the HTTP status | Inspect persistent state after failed and competing writes |
| Data modeling | Add one entity | Explain uniqueness, parent ownership, deletion behavior, and legacy data |
| Time | Use today's date | Distinguish effective time, recording time, current configuration, and saved evidence |
| Concurrency | Notice a Version field | Identify every writer that can break a shared invariant and test a losing writer |
| Operations | Run the happy path | Explain retries, failure recovery, and what survives a process restart |
| Communication | Describe changed files | State the user outcome, evidence, and remaining limitations clearly |

You can work on different columns at different speeds. Being comfortable with C# syntax does not mean the data model or concurrency rules are automatically familiar. Practice the unfamiliar part directly.

## Write a small review

Use one implemented feature and answer:

“A user can now ____. The request is allowed when ____. The database records ____ together. If ____ changes concurrently, ____. I verified this with ____. The feature still does not promise ____.”

Then remove the blanks and make it sound like a normal explanation to a teammate. Good engineering communication connects the user's promise to evidence in the implementation.
