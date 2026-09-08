# Lesson 3: Make a whole decision stick

This lesson follows MS-04, MS-05, MS-06, MS-26, MS-27, and MS-30. Start with onboarding. Return to credentials and hiring once the first example makes sense. The implementation lives in [OnboardingWorkflowEndpoints](../../src/Atlas.Api/Endpoints/OnboardingWorkflowEndpoints.cs); its behavior checks live in [WorkflowStoryTests](../../tests/Atlas.Tests/Integration/WorkflowStoryTests.cs). Consult [progress](progress.md) for the actual verification status.

## First picture: a checklist with two boxes

You review an identity document and bank details together. The request says “complete these two boxes.” If the bank item belongs to a different contract, completing the identity item anyway would misrepresent your submission.

The batch endpoint therefore has two phases:

1. Check the entire request: caller permission, item count, duplicate IDs, note lengths, parent membership, versions, and completion state.
2. Change the validated items, then save them together.

Said another way: inspect every item before checking any box. Once all checks pass, submit the set as one database operation.

This is why the loop that calls `Complete` appears after membership and state checks. A loop that validates and saves one item at a time can leave the first item complete when the second one fails.

## Trace an actual request

Read `MS04_BatchValidatesEveryMemberAndReturnsTheWholeChecklist` first. It constructs a request with one real checklist ID and one nonexistent ID. Predict the result: 404, and zero completed items. The test checks persistence through a fresh database scope, rather than merely checking the response code.

Then follow the endpoint:

| Stop | Question answered | Evidence in code |
| --- | --- | --- |
| Contract lookup | Does this parent exist and can I see it? | `FindAsync` plus `CanViewClient` |
| Manage check | Am I allowed to complete its checklist? | `CanManageClient` |
| Body checks | Is the batch well formed? | 1–20 items, distinct IDs, bounded notes |
| Child query | Are all these items on this contract? | Both `ContractId` and item IDs in `Where` |
| Version/state checks | Does my view still match the records? | Expected versions and `IsCompleted` |
| Domain transition | What changes when an item completes? | `OnboardingItem.Complete` |
| Save | Can the entire update commit? | One `SaveChangesAsync` |
| Response | What is the checklist now? | Whole checklist, including untouched items |

Notice the single `now` value. Two items completed by one operation receive the same timestamp. The clock is captured once rather than read during each loop iteration.

## A second request can arrive after your checks

Suppose both Alex and Blair read item A at Version 1. Alex completes it. Blair's request still holds Version 1. If Blair could save without checking that original version, Blair could replace the completion note without realizing that Alex had already reviewed the item.

`IVersioned` is the repository's solution for this class of stale write. Read [AtlasDbContext](../../src/Atlas.Infrastructure/AtlasDbContext.cs), especially `BumpVersions` and the concurrency-token configuration. EF includes the original version in the update condition. If the database version changed, the update affects no matching row and EF reports a concurrency conflict.

Conceptually, the database update asks:

```sql
UPDATE OnboardingItems
SET IsCompleted = 1, Version = 2
WHERE Id = @id AND Version = 1;
```

This SQL is an explanation of the condition, not a migration or a script to run. The real update also includes changed metadata and uses EF's generated commands.

The early expected-version check gives a clear failure when a request is already stale at load time. The database condition catches changes that happen after that check. You need both places because they operate at different moments.

## Replay the race slowly

The test `MS04_StaleBatchRollsBackItsOtherItem` uses two contexts:

1. Context A reads items A and B.
2. Context B completes A and saves.
3. Context A tries to complete both A and B using its older data.
4. Saving A's batch fails because item A changed.
5. A fresh read confirms that B is still incomplete.

The last assertion is the point of the test. “An exception occurred” alone would not prove that the other item stayed unchanged. When testing atomicity, inspect the records that might have been partly written.

**Pause and explain:** which condition detects stale data, and which boundary prevents half a batch from persisting? The version detects the stale item; the save transaction keeps the batch together. They solve related but different problems.

## A template is a recipe; a checklist is the copied work

MS-06 adds a country template. A new contract copies template values into its own checklist rows. The template is configuration; those checklist rows belong to a particular onboarding operation.

Imagine a template title changes from “Identity check” to “Photo ID and identity check.” A contract created yesterday keeps yesterday's title. A contract created tomorrow receives the new title. Existing items do not suddenly change meaning because someone edited configuration.

Read the contract creation block in [ContractEndpoints](../../src/Atlas.Api/Endpoints/ContractEndpoints.cs). Find the construction of `new OnboardingItem`. That allocation creates new IDs and independent state. Reusing template item IDs would collapse two different responsibilities into the same record.

An absent template has Version 0 in the response and uses the default checklist. First configuration creates a header at Version 1. Two initial writers can both observe “absent,” so a version check in memory alone cannot decide the winner. The unique country key prevents two headers. Later replacements use the header's concurrency token.

Replacing the template also touches its header version when the changes are in child rows. This is intentional: the version describes the whole template, not merely the scalar columns on the header.

## Sometimes two different records share one rule

Hiring pause is a useful next example. A contract-creation request reads “country active.” A different request pauses hiring. Without coordination, the creation request might still save based on its older observation.

Creation and activation therefore touch Country.Version when saving. The pause operation changes the same country row. One writer using a stale country version conflicts. Country is the common record on which these competing decisions meet.

This does have a tradeoff: unrelated hiring operations in one country may conflict and require a reload. The implementation prioritizes a simple, visible consistency rule. A future design with more write throughput would need to preserve the same rule while changing how requests coordinate.

Payroll for already active contracts remains eligible while hiring is paused. Readiness also includes the pause, so the preview and activation action explain the same restriction. A repeated pause request with the current version is a no-op and does not manufacture another version change.

## Authorship is server evidence

MS-05 notes contain an author API-user ID and timestamp. Neither value comes from the submitted body. The principal identifies the authenticated API user; the clock supplies the timestamp. The body contains only the note text.

Read `MS05_NotesAreAuthoredByTheCallerAndDoNotChangeContractVersion`. Its body attempts to nominate another author. The saved note still names the authenticated caller. It also checks that appending a note leaves Contract.Version unchanged: a general note does not change employment terms or the contract lifecycle.

These notes are visible to client viewers. They are shared operational notes, not a private channel. There are no API edit/delete routes for them, but that does not make them tamper-proof against a database operator. Use precise language when describing a guarantee.

## Credentials have a lifecycle too

Read [ApiKeyAuthenticationHandler](../../src/Atlas.Api/Auth/ApiKeyAuthenticationHandler.cs) and [ApiUserEndpoints](../../src/Atlas.Api/Endpoints/ApiUserEndpoints.cs). Authentication checks both active state and expiration. At the exact expiration instant, the credential stops authenticating new requests. A null expiration preserves the legacy non-expiring behavior.

Replacement creates a new credential and deactivates the old credential in one save. It copies role, client scope, and expiration. It does not grant a new lifetime or broader permissions. A unique `ReplacesApiUserId` prevents two replacement rows for the same credential.

The new secret appears once in the creation response. List and detail responses use masked values. Retrying replacement returns a conflict; it does not return the secret again. This is a deliberate difference from the payment replay workflow in the next lesson.

Replacing a key does not cancel a request that has already authenticated. Also, API-user IDs are credential identities, not proof that two distinct humans exist. Keep those limits in mind when reading later approval and notification features.

## Practice with small steps

Run the story suite using `python scripts/bootcamp.py test MS-04` once that suite is registered in the progress manifest. The CLI's output names the actual test command; it preserves failures instead of hiding them.

1. Read the stale-batch test without running it. Write down the final state of A and B.
2. Run it and compare your prediction with its assertions.
3. Put a breakpoint before `SaveChangesAsync`. Inspect the entity's current and original Version values.
4. Explain the same behavior without saying “EF,” “token,” or “transaction.” For example: “My update only counts if the checklist is still the version I reviewed; otherwise none of my batch is saved.”
5. Return tomorrow and draw the two-context timeline from memory.

For a review exercise, propose what should happen when a template batch contains the same type twice under different letter casing. Find the normalization and distinct-type check before answering. Then explain why note authorship and contract ownership are separate checks even though both involve IDs.

You are ready to continue when you can explain one successful write, one malformed request, one forbidden request, and one stale write without conflating their causes.
