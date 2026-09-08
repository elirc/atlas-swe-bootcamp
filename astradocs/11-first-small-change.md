# Your first small change: one requirement, one route

**One objective:** prepare a small, reviewable change without reading every feature. This is an exercise specification; these docs do not implement it for you.

## A practice requirement

“When creating a country, its code must contain exactly two ASCII letters after trimming. Continue accepting lowercase letters and store the uppercase form.”

The current POST check verifies length and blank input, so a two-character value such as `1A` can pass that particular check. Our practice rule is about letter characters, not membership in the real list of assigned country codes.

## Pass 1: locate the behavior

Open [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs). Find `request.Code`. Locate validation, normalization, and the duplicate check. Do not change the `Country` schema or unrelated routes.

Write the five questions from [the hub](README.md): caller, handler, checks, persistence, response. For a rejected `1A`, no new country should be saved by that request.

## Pass 2: make examples before code

| Input | Required outcome |
| --- | --- |
| `nl` | Valid; normalized to `NL` if available |
| ` NL ` | Valid after trimming if available |
| `1A` | 400 with a `code` error |
| `ABC` | 400 with a `code` error |
| blank or null | 400 with a `code` error |

Prepare a valid name, currency, and rates so another validation error does not hide the code behavior. Use isolated test data: an existing `NL` record would correctly trigger the separate duplicate rule.

## Pass 3: implement the smallest useful rule

Keep the existing null/blank guard before accessing characters. Validate the trimmed value. If using a character helper, check whether it accepts more than ASCII letters; the practice requirement is deliberately specific.

Hint 1: you already know where the 400 error dictionary is built.

Hint 2: a length check and a character check answer different questions.

Hint 3: keep normalization and validation consistent about whitespace and case.

Try one hint at a time. You do not need the complete answer immediately.

## Pass 4: verify and explain

Add a regression that fails for `1A` under the old implementation, then succeeds after the change. Assert the status and error field. Preserve the existing valid round-trip behavior. Run the country tests, then any other affected validation suites.

Inspect `git diff` for unrelated changes. In your review note, explain the trigger, new behavior, evidence, and the fact that this does not verify a real assigned country code.

## Say it again

One route. One explicit rule. A failing example. A small implementation. Evidence. An explanation.

You can do each pass in a separate session. For more exercises after this one, use the [existing practice backlog](../docs/learning/practice-backlog.md).
