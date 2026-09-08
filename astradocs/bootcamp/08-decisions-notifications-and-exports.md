# Lesson 8: durable decisions, personal inboxes, and background exports

Follow SS-16, SS-17, and SS-18 through this lesson. The learning target is to explain what happens when an operation succeeds, fails, races another request, or survives a process restart. Start with the user-visible behavior before learning the implementation vocabulary.

## One decision, two durable outcomes

Approving an amendment changes a contract. It may also insert a salary-history record. The decision feed adds evidence: which API identity approved it, what values changed, and which workflow produced the change.

Those records must agree. A salary change without its promised decision event is incomplete. An event claiming a salary changed when the change rolled back is false evidence. The application therefore treats the business change and event as one transaction.

Open [ContractDecisionWriter.cs](../../src/Atlas.Api/ContractDecisionWriter.cs). Read the method in this order:

1. Begin a transaction.
2. Save the business changes, including the contract version.
3. Construct a small payload from copied before/after values.
4. Insert the event with the resulting contract version.
5. Save the event and commit the transaction.

The first `SaveChangesAsync` sends SQL, but it does not commit the outer transaction. If the second save fails, disposing the uncommitted transaction rolls back the first save too. This is why “it called SaveChanges” is not enough information to determine durability.

Draw a rectangle around both saves. Label the rectangle “transaction.” Put the commit at its right edge. Now draw a failure arrow between the saves that leaves the rectangle through rollback. Both changes should disappear along that arrow.

## Before and after must really be before and after

The endpoint captures `ContractDecisionValues` before invoking the transition. It passes that value object to the writer after the transition. A reference to the same tracked entity would not preserve old values: reading it later would show the changed object.

For an amendment from 100 to 140, predict this payload fragment:

```json
{
  "before": { "monthlySalary": 100 },
  "after": { "monthlySalary": 140 },
  "effectiveDate": "2026-10-01"
}
```

The real payload also includes the other allowlisted contract fields. It excludes reasons, request bodies, credentials, and receipt URLs. Read the record declaration and verify the allowed fields yourself. The payload has schema version 1 and an 8 KiB cap. The system fails the whole decision if it cannot record the promised bounded evidence.

There are four capture entry points: activation, direct termination, termination-request approval, and amendment approval. The event kind describes the outcome; the source kind distinguishes how it happened. A direct termination and an approved termination request both produce `terminated`, with different source kinds and source IDs.

This feed begins with recorded actions after the feature is present. It does not manufacture events for existing contracts or audit arbitrary database edits. The database sequence orders recording. An effective employment date answers a different question. A future-effective amendment can be recorded before a termination whose effective date is earlier.

## A useful failure test

Find `SS16_EventFailureRollsBackAmendmentSalaryHistoryAndContract` in [AdvancedStoryTests.cs](../../tests/Atlas.Tests/Integration/AdvancedStoryTests.cs). It installs a SQLite trigger that rejects event insertion. The test calls the actual approval endpoint and checks that the contract salary, salary history, amendment status, and event table all remain unchanged.

This test is stronger than checking that the event builder returns a JSON string. It examines the promise at the failure boundary. After removing the trigger, it retries and checks the complete successful result.

Practice explaining the test without mentioning a trigger: “We deliberately make the evidence save fail, then prove the salary change did not sneak through.” Next explain the technical mechanism and point to the outer transaction. Both explanations should describe the same guarantee.

## The inbox is derived from committed events

The decision endpoint does not send an in-memory notification and hope the process stays alive. [NotificationService.cs](../../src/Atlas.Infrastructure/NotificationService.cs) reads committed events that lack a notification. It processes at most 100 oldest missing events per pass.

If the process crashes before inserting a notification, the event is still missing one and remains eligible. If it crashes after insertion commits, the existing notification prevents another insert. A unique source-event/client index is the final duplicate guard when two dispatchers discover the same missing event concurrently.

On a competing insert, the service uses a fresh context to verify that the other dispatcher actually created the notification. An unrelated database failure does not count as successful delivery. Read the exception branch and explain why a blanket `catch { return success; }` would lose work.

The titles use fixed templates such as “Contract amendment approved.” They do not contain salaries or free-text reasons. Links are built from known local contract routes. A notification is a convenient way to discover a change; the scoped contract and decision endpoints provide details.

There is no claim of exactly-once delivery to an external provider. This feature creates deduplicated rows in the same database. There is no email, push service, or external recipient.

## Personal read state is a different kind of write

A client viewer cannot activate a contract, but can mark their own notification as read. These permissions are consistent because the writes affect different things. One changes employment state for the client. The other changes the caller's personal view of an inbox.

Read [ContractDecisionEndpoints.cs](../../src/Atlas.Api/Endpoints/ContractDecisionEndpoints.cs). The reader ID comes from authenticated claims. The request cannot choose another reader. A unique notification/API-user key makes repeated read marking return 200 without duplicate rows.

Consider two identities, A and B. A marks a notification read. A's unread count becomes zero; B's remains one. The notification row is shared, and read-marker rows are personal. Sketch one notification with two optional marker rows instead of drawing two copies of the notification.

Replacing an API identity creates a new identity with its own read state. A later migration feature could transfer markers deliberately; replacement currently does not infer that two API identities represent the same human.

The inbox uses a descending sequence bookmark. The decision feed uses an ascending sequence bookmark. They serve different reading patterns: browse recent notifications backward, or consume new decisions forward. Scope is applied before paging in both cases.

## Background exports reuse a durable job protocol

SS-18 adds an export job kind to the payroll job mechanism described in [lesson 6](06-payroll-evidence-and-durable-work.md). First inspect [DurableJobService.cs](../../src/Atlas.Infrastructure/DurableJobService.cs), then [DurableJobService.Exports.cs](../../src/Atlas.Infrastructure/DurableJobService.Exports.cs).

The useful sequence is:

```text
submit → claim lease → recheck requester → read consistent data
       → prepare bounded JSON → renew/fence ownership → save artifact and success
```

A lease is temporary permission for one worker to finish a job. It is not enough to check that permission once at the beginning. Another worker may take over after expiry. The final conditional database write, called the fence, requires the same unexpired lease token immediately before saving effects. A stale worker cannot save an artifact after another worker has become the owner.

Read `CommitExportAsync`: the fence, artifact insertion, and Succeeded state share a transaction. If artifact insertion fails, the job does not remain falsely marked Succeeded. The corresponding test rejects artifact insertion and checks both sides of that promise.

## Authorization is checked over time

At submission, the caller must manage the requested client. The job stores both requester identity and client scope. Before reading export data, the worker reloads that identity and checks that it is still active, unexpired, and permitted to manage that client. The final commit checks again. Polling and downloading require current authentication, matching ownership, and current management permission.

An unguessable job ID is not authorization. A platform identity does not automatically receive another identity's export. The job's owner matters even when the caller has a broad role.

Explain this with a timeline: Monday, a manager requests an export. Tuesday, the key is revoked. Wednesday, somebody tries to download with that key. Monday's permission does not authorize Wednesday's request. A response already authorized and streaming cannot be pulled back by a later revocation, but a new download request must pass current checks.

## Export content is a deliberate data contract

The export projects explicit fields rather than serializing EF entities. It contains the client's contracts, IDs/names/emails of their referenced workers, and the client's payslips and invoices within the requested payroll periods. It excludes worker DOB, documents, API identities and keys, receipt URLs, and unrelated workflows.

The contract collection is not filtered to the payroll period: the specified feature includes all client contracts. The period limits only payslips and invoices. That distinction belongs in the API contract, not in an assumption made by a consumer.

Every collection is read in one database read transaction. Otherwise a contract read before an update and an invoice read after it could form a mixed view. Stable ordering makes the output easier to review. A checksum describes the exact serialized bytes; it does not prove that the underlying business data is correct.

The background read uses [SqliteReadSnapshot](../../src/Atlas.Infrastructure/SqliteReadSnapshot.cs), which explicitly begins a deferred transaction. With WAL enabled, the reader keeps its original view while a separate connection updates the lease. An ordinary immediate transaction would reserve the writer lock and could obstruct the very renewal that keeps the work alive. The final fenced write still uses its own write transaction. The lease-renewal test holds a read open, performs a renewal, and checks that the old reader sees its original version while a fresh reader sees the newer version.

There are two caps: 5,000 total data rows and 5 MiB of serialized UTF-8. Count rows across collections rather than allowing 5,000 in each. The bounded output stream rejects output that grows past the byte limit. Jobs report explicit row-limit or byte-limit failures.

Artifacts expire 24 hours after generation. At exact expiry, an owned download returns 410 even if the cleanup worker has not cleared the bytes yet. Cleanup removes expired bytes in bounded batches while preserving expiry and checksum metadata. A new export after expiry uses a new reference; replaying the old reference still refers to the old job.

## Retrieval practice

Answer once in prose and again with a diagram:

1. Why does event insertion failure roll back a salary change already sent to the database?
2. What would break if before-values were a reference to the mutated contract?
3. How does a notification survive an API restart immediately after approval?
4. What protects two dispatchers from creating duplicate notifications?
5. Why can a viewer mark a notification read but not approve an amendment?
6. What does the final lease fence prevent?
7. Where is export ownership checked after submission?
8. Why must download enforce expiry independently from cleanup?

For the review exercise, choose one failure case and write a five-sentence PR description: user promise, changed code path, transaction boundary, test evidence, and remaining limitation. Keep the claims no broader than the test and implementation support.
