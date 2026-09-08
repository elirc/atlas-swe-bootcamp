# Progress journal

Copy the entry below into a personal note or a new file for each session. Keep secrets and real payroll data out of examples.

## Session template

```text
Date / lesson / task:
What I intend to understand:
My prediction before running anything:
Exact command or request:
Observed result:
Relevant source and test:
What I changed and why:
Evidence that it works:
What the evidence does not establish:
One alternative I considered:
Reviewer feedback and my response:
One explanation I can now give without notes:
Next smallest step:
```

## Review your independence

| Skill | With guidance | Independently | Can teach with evidence |
| --- | --- | --- | --- |
| Trace a request through its layers | Locate a named method | Find the responsible code | Explain the boundary and assumptions |
| Reproduce a defect | Run a supplied test | Construct distinguishing input | Explain what the test excludes |
| Deliver a feature | Follow a checklist | Own auth, data, tests, and docs | Review another implementation |
| Handle failure | Recognize 409/500 | Explain races and commit outcomes | Design and defend a recovery policy |
| Make a design decision | Compare given options | Choose from requirements | Revisit the choice using new evidence |

Mark a level only when you can link a concrete change or explanation. You can be independent in C# syntax while still needing guidance on concurrency; that is useful information, not a failure.

## Worked journal entry

**Prediction:** a very large positive `withinDays` will be accepted because it is not negative.

**Observation:** the old endpoint calls `DateOnly.AddDays` and can overflow. The revised endpoint returns 400 with an error on `withinDays`.

**Evidence:** `InvalidWindows_Return400InsteadOfOverflow` includes the maximum integer and a date at the representable boundary. A zero-window test protects the valid boundary.

**Limitation:** those tests do not prove that every direct `DateOnly.AddDays` elsewhere is safe.

**Transfer:** validate representability before arithmetic, and distinguish endpoint input checks from reusable domain guarantees.

## Monthly review

Choose your strongest and weakest change. Re-explain each without reading the original solution. Identify one repeated gap, choose one backlog exercise targeting it, and ask a reviewer to evaluate that specific skill. Do not infer readiness from total tests, lines changed, or hours alone.
