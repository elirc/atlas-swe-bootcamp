# Repeat the important ideas in different ways

Use one round at a time. Try an answer, then look at [the explanations](13-answers.md). An incomplete answer helps identify which page to revisit.

## Round A: fill a small blank

1. The project hosting HTTP endpoints is __.
2. A worker's employment agreement is an __.
3. `db.Countries.Add(country)` tracks a new object; __ persists it.
4. The country read handler returns __ when its lookup finds no record.
5. The contract's `Activate` method permits the initial state __.
6. One person's pay result in a run is a __; the company's bill is an __.

## Round B: put the cards in order

For a successful country creation, order these:

- Save tracked changes.
- Validate request fields.
- Return 201.
- Construct the country and call Add.
- Receive the POST as an authorized platform admin.
- Normalize the code and check for an existing country.

Then draw the order as arrows. Finally explain it without the cards.

## Round C: same concept, changed example

7. You replace `PH` with `ph` in a country lookup. Which code handles the difference?
8. You remove the API key. Why would changing the route's country code not solve the failure?
9. You call `contract.Activate(now)` in a direct domain test. Has anything been saved to SQLite?
10. You change the country response's displayed name in an experiment. Does changing the response shape alone update a stored country?
11. You add a second required onboarding item. Which code reads whether required items are incomplete before activation?
12. A test creates a country successfully. Why might it be absent when you inspect the Development database?

## Round D: find the evidence

Locate each item in the source without relying on remembered line numbers:

- Where country routes are registered on the app.
- Where the country route group requires authentication.
- Where only country creation additionally requires PlatformAdmin.
- Where country response fields are chosen.
- Where required onboarding is checked before activation.
- Where the entity changes its status to Active.
- Where the test factory selects an in-memory database.

Finding evidence is part of understanding. You do not need to memorize the exact source text.

## Round E: explain aloud

Give a 30-second explanation of a country GET. Then give a two-minute explanation that adds authentication, dependency injection, and response mapping. They should describe the same behavior at different levels of detail.

Now explain it as a clerk looking up a catalog entry. Name one thing the analogy leaves out, such as authentication's database query.

## Round F: change the numbers

For a base gross salary of 2,000, employer rate 12%, and deduction rate 15%, compute employer cost, deductions, net pay, and total employer cost. Explain why a complete invoice may have a different total.

## A repeatable schedule

At the end of a session, answer two items. At your next session, answer them again before reading. Later in the week, answer a variant from another round. Adjust the gaps and number of questions to your pace; there is no pass/fail deadline.

Record answers as “can explain,” “can find with a hint,” or “revisit.” A missed item should point to a specific concept, not a judgment about your ability.
