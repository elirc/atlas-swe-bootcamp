# Lesson 9: time, policies, and typed configuration

This lesson covers SS-12 through SS-15. The repeated question is: **what does this answer mean at this date, under this configuration?** The examples are document renewal, document coverage, payroll budgets, and custom contract fields.

## Renewing a document does not erase the old document

A document record contains metadata such as its type, name, issue date, and expiry date. Renewing it creates a new record with a link to the previous record. The old record remains readable. The replacement inherits the same worker and document type, so a renewal cannot silently move evidence between people or categories.

Open [ComplianceWorkflowEndpoints.cs](../../src/Atlas.Api/Endpoints/ComplianceWorkflowEndpoints.cs). Find the renewal route. Follow the parent checks before reading the transition checks. A document ID must actually belong to the worker ID in the route; knowing both IDs does not make them a valid pair.

The unique `SupersedesDocumentId` index in [AtlasDbContext.Bootcamp.cs](../../src/Atlas.Infrastructure/AtlasDbContext.Bootcamp.cs) prevents two renewals from branching off the same predecessor. The preliminary “already renewed” check gives a helpful response. The unique constraint closes the race when two requests both pass that check before either saves.

Picture a chain:

```text
Passport A → Passport B → Passport C
```

Each arrow means “the next record supersedes the previous record.” It does not mean “delete the previous record.” The history reader traverses the chain with a visited set and a size bound. Those checks prevent malformed stored relationships from causing an endless traversal.

## There are several clocks in one document question

Suppose A expires September 15. B is issued September 1, expires next September, and is recorded September 3.

On September 2, the database did not yet know about B. A report with an as-of cutoff of September 2 should not use a record entered on September 3 to silently remove A from that earlier view. On September 3, B can become the current recorded renewal.

Read [ComplianceQueries.cs](../../src/Atlas.Infrastructure/ComplianceQueries.cs). `CurrentDocumentsAt` considers recording time as well as successor relationships. Read its predicate slowly, replacing each variable with A and B from the example.

Now distinguish three questions:

| Question | Relevant value |
| --- | --- |
| Had the system recorded this document yet? | CreatedAtUtc |
| Had the document been issued by the assessment date? | IssuedDate |
| Does it have enough remaining validity? | ExpiryDate and policy minimum days |

These dates are related but cannot substitute for one another. A future-issued document is not usable merely because someone entered it early. A newly entered document can describe an earlier issue date without proving the system knew it earlier.

The existing historical report fixture was adjusted to provide explicit recording dates. Tests that ask historical questions need historical setup; a default “created now” timestamp can accidentally make the fixture contradict the question.

## Start coverage from requirements

A country document policy lists required types, whether expiry is required, and minimum remaining days. Coverage evaluates the policy against the worker's current recorded documents at the chosen date.

The query must begin with policy requirements. If you begin with the documents that exist, a completely missing required type can disappear from your result. That would make absence look like success.

Open [DocumentCoverage.cs](../../src/Atlas.Domain/Services/DocumentCoverage.cs). Find the loop over requirements. For each requirement, the function considers matching candidates and produces either a qualifying document or explicit blockers. One adequate candidate is enough; another inadequate candidate of the same type does not override it.

Work this example by hand:

- Passport requires expiry and 30 days remaining.
- WorkPermit requires expiry and 0 days remaining.
- Assessment date is September 1.
- One passport expires September 15.
- Another passport expires October 1.
- No work permit exists.

The passport requirement is covered by the October 1 candidate. The work-permit requirement reports a missing type. The overall assessment is not covered. Counting the number of documents would tell you very little about this outcome.

The function uses day-number differences for remaining days. That makes the boundary easy to reason about: exactly 30 remaining days satisfies a 30-day minimum. Test exact equality as well as one day below and above.

An unconfigured country policy returns `not-configured`, rather than pretending an empty requirement list proves compliance. This feature assesses stored metadata against configured rules. It does not authenticate a real-world document or add a new activation gate.

## A budget compares an intention with a defined actual

A payroll budget is keyed by client, year, month, and currency. Its amount is a plan. The report's actual is the sum of saved `TotalCost` from completed payroll payslips for that client and period. It excludes draft runs and does not add management fees or convert currencies.

Those choices define the meaning of variance. A number labeled “actual” is incomplete until you know which records and amounts it includes. Read the report query in [ClientConfigurationEndpoints.cs](../../src/Atlas.Api/Endpoints/ClientConfigurationEndpoints.cs) and identify every part of that definition.

The report unions keys from budgets and actuals. A budgeted month with no completed payroll has actual zero. A completed-payroll month without a budget has a null budget and `no-budget` status. An inner join would incorrectly drop one or both cases.

Now consider budget 1000 and actual 899.999. Displayed utilization rounded to two decimal places is 90.00%. The exact actual is still below the 90% threshold, so the status remains `within-budget`. Classifying the rounded display would incorrectly label the month `near-budget`.

Use this rule in your own words: **decide with the underlying value; round for display afterward.** Find that ordering in the implementation and the exact-boundary example in [ComplianceConfigurationStoryTests.cs](../../tests/Atlas.Tests/Integration/ComplianceConfigurationStoryTests.cs).

Budget updates use an expected version. Initial creation expects 0 and stores version 1. A competing edit with an older version receives a conflict instead of overwriting a newer budget. Explain who supplies the expected version and who controls the resulting stored version.

## Custom fields are typed data, not arbitrary payroll instructions

A client can define additional contract fields of type text, number, or choice. The key is a bounded lowercase ASCII identifier. The label is for display. Choice options are stable codes. A definition belongs to one client, and a contract can use only its client's definitions.

For example:

```text
key: team       type: choice   options: ops, eng
key: allowance  type: number
key: note       type: text
```

The saved value for `allowance` is a decimal JSON number, not a quoted number string. The storage model has separate nullable columns for text, number, and choice. A database check constraint requires exactly one of those columns to be populated. That prevents ambiguous rows even if a later writer bypasses this endpoint's normal validation.

Typed validation catches several different mistakes: sending a number for a choice; sending an undefined choice; sending a decimal with more than four places; sending a field key defined for another client. These are input errors, and the whole replacement is rejected before deleting existing values.

The update replaces the complete supplied set. An empty list intentionally clears all values. The endpoint checks the contract version, validates every supplied entry, and only then stages old rows for deletion and new rows for insertion. It explicitly marks new child rows Added because nondefault GUIDs do not automatically communicate insertion intent to EF in every tracked-parent scenario.

Custom values do not change payroll calculations, salary history, or employment terms. A field named `allowance` is merely client-defined metadata in this feature. Giving a field a financially suggestive name does not connect it to payroll logic. That boundary keeps the feature understandable and prevents clients from accidentally changing financial behavior through configuration labels.

## Explain the same feature three ways

Choose custom-field replacement for this exercise.

**User explanation:** “I can save a complete set of extra details, and a mistake in one field will leave my previous details intact.”

**Request trace:** “The route loads the contract, checks client access and management permission, compares the expected contract version, validates all keys and typed values, then saves the replacement set with a version change.”

**Database explanation:** “Old rows are Deleted, replacements are Added, and the contract is Modified. One save transaction commits them together, while uniqueness and the one-value check constrain the stored rows.”

Do not memorize those sentences. Close the document and reconstruct them using a different example such as a `cost_band` choice field. Then find the code supporting each claim.

## Exercises for a second visit

1. Draw a renewal chain and mark the recording dates separately from the issue dates.
2. Predict coverage for a required document that expires exactly on the assessment date with a minimum of zero days.
3. Explain why an absent policy is labeled separately from a covered policy.
4. Predict a budget report containing one budget-only month, one actual-only month, and one month with both.
5. Explain why 899.999 against 1000 does not become near-budget merely because its displayed percentage rounds to 90.00.
6. Send a replacement containing one valid field and one invalid field. Predict the previous saved set before reading the test.
7. Describe the difference between a metadata field called `salary` and the contract's actual MonthlySalary property.

For your journal, record one prediction that was wrong. Write which assumption caused it, what evidence corrected it, and one similar example you will try tomorrow. That entry teaches more than copying a successful response.
