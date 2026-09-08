# A guide for pairing, mentoring, or asking an assistant

The aim is to help the learner build an explanation they can reproduce. Let them choose the pace and how much detail to add. Repetition can revisit the same idea through a different example or form without making the conversation childish.

## A short pairing session

1. Pick one outcome: “Explain where a country GET gets its data.”
2. Ask for a prediction before running a command.
3. Read one short handler together.
4. Let the learner trace it with a diagram or ordinary sentences.
5. Change one input, such as `PH` to `ph`, and predict again.
6. End with one thing understood and one specific open question.

Do not combine HTTP, EF, concurrency, async internals, and database migrations into the first explanation. Keep a visible “later” list for questions outside the current objective.

## Repeat without only copying the same paragraph

| First explanation | Second explanation | Third explanation |
| --- | --- | --- |
| “The handler looks up a country.” | Draw caller → handler → database → response | Trace `FindAsync` in the source |
| “Add does not save.” | Arrange `new`, `Add`, `SaveChangesAsync` cards | Compare object state before and after a save |
| “A valid key is not permission for everything.” | Use the badge/files analogy | Compare country GET and POST policies |
| “The domain method changes state.” | Read the Draft → Active transition | Find the endpoint's separate save call |

Name the same concepts consistently. Introduce an alternative term only by connecting it: “This response model is also often called a DTO.”

## A hint ladder

If the learner cannot find the country lookup, first name the project, then the file, then the method, then the line. Stop as soon as they can continue. A complete answer is useful when requested, but should not replace every opportunity to inspect the source.

When an answer is wrong, locate the assumption. For “Add writes the row,” ask the learner to find the next database-related call and compare the two operations. Correct the claim directly and have them retell the sequence.

## Copyable prompts

> Onboard me using `astradocs`. Today I only want to understand a country GET. Explain it in plain language, then show a diagram, then point to the real code. Give me one checkpoint at a time and wait for my answer.

> I understand the story but not this C# line: __. Expand compact syntax, name each value's role, and explain what changes in memory or storage. Then give me a nearby example to predict.

> Quiz me on the difference between API, domain, and persistence using Atlas examples. If I miss one, explain it a different way and ask a similar question later. Do not describe my ability based on one answer.

## A useful handoff note

```text
Current example:
Can explain without help:
Can find with a hint:
Needs another explanation:
Next small example:
```

Judge readiness by observable work: locating a handler, explaining its checks, reproducing behavior, and making a small reviewed change. Use the [broader roadmap](../docs/learning/roadmap.md) when those foundations feel familiar.
