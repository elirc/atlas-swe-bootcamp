# Verification record for the 75-story build

All 75 stories have implementation code and passing relevant behavior tests. This record separates the earlier full run from the later correction run. It does not claim a new full-suite pass after the corrections.

## Observed results

| Check | Result | What this establishes |
| --- | --- | --- |
| Final Release solution build | Passed; zero warnings, zero errors | All projects compile together, including the latest tests |
| Full run before final corrections | 586 executed: 569 passed, 17 failed, zero skipped | Broad regression evidence; all 112 tests in the eight story suites passed |
| Focused run after corrections | 101 executed: 101 passed, zero failed/skipped | Every earlier failing case passes, along with affected regression suites and new reliability/limit tests |
| Development startup and HTTP smoke | Passed against a new isolated SQLite database | Fresh-schema creation, sample seeding, readiness, currency filtering, client scope, and read-only payroll simulation work through the real entry point |
| Learning helpers | Python compilation, quiz loading, launcher help/dry-run passed | The launcher resolves the local SDK; 45 recall questions are available |
| Documentation | Local inline link check passed | Referenced local files exist; the checker does not validate web URLs or heading anchors |
| Whitespace | `git diff --check` passed | No whitespace errors in the tracked diff |

The full run's 17 failures were nine payroll edge cases, seven payroll endpoint cases, and one leave boundary case. The country catalog already accepted two-character identifiers containing digits; a new shared worker validator accidentally rejected them. A shared leave assessment also changed an existing specific error message. Both were corrected. The payroll test setup now checks each HTTP status before deserializing its success response, exposing the first failure instead of a later misleading JSON exception.

The correction report was compared with the original report by exact test name: all 17 previously failing names occur as Passed in the correction run. There were no skipped tests in either run. Four additional tests were included in the correction run; some existing tests also gained assertions. These two runs overlap, so adding their totals would misrepresent the number of distinct tests.

## What ran in the correction pass

| Suite | Passed | Reason for including it |
| --- | --- | --- |
| PayrollEndpointTests | 13 | Existing payroll setup and behavior affected by worker validation |
| PayrollEdgeCaseTests | 14 | Configured country identifiers and payroll boundary behavior |
| BoundaryValidationTests | 26 | Existing validation contracts, including the leave message |
| ClientAndWorkerEndpointTests | 5 | Shared worker creation validation |
| LeaveEndpointTests | 17 | Shared leave assessment behavior |
| AdvancedStoryTests | 12 | Allocation precision, country-compatible imports, audit, notifications, and snapshots |
| ExportStoryTests | 6 | Cross-client isolation, expiration, revocation, failure rollback, total row limit, and serialized byte limit |
| PayrollReliabilityStoryTests | 8 | Simulation/evidence, retry receipts, stale leases, rollback, and a deferred SQLite reader allowing renewal |
| **Total** | **101** | **All passed** |

The new deferred-reader test uses file-backed SQLite in WAL mode. It holds one read snapshot open while another connection renews a lease, proves the old reader retains its view, then proves a fresh reader sees the change. The new export cap tests independently exceed the total row limit and byte limit. The allocation assertions reject an extreme amount whose individual split cannot be represented as exact decimal cents.

## Reproduce the checks

Run these from the repository root. If `dotnet` is not on PATH, use the SDK path shown by `python scripts/learn.py doctor`; this workspace used `C:\Users\E\.dotnet\dotnet.exe`.

```powershell
dotnet build Atlas.slnx -c Release --nologo --no-restore -p:UseSharedCompilation=false -m:1

dotnet test tests/Atlas.Tests/Atlas.Tests.csproj -c Release --no-build --no-restore --nologo --filter 'FullyQualifiedName~PayrollEndpointTests|FullyQualifiedName~PayrollEdgeCaseTests|FullyQualifiedName~BoundaryValidationTests|FullyQualifiedName~ClientAndWorkerEndpointTests|FullyQualifiedName~LeaveEndpointTests|FullyQualifiedName~AdvancedStoryTests|FullyQualifiedName~ExportStoryTests|FullyQualifiedName~PayrollReliabilityStoryTests' --logger 'trx;LogFileName=bootcamp-corrections.trx' --results-directory artifacts/bootcamp-tests

python -m py_compile scripts/bootcamp.py scripts/bootcamp_progress.py
python scripts/bootcamp.py quiz
python scripts/bootcamp.py serve --dry-run
python scripts/learn.py check-docs
git diff --check
```

The commands assume dependencies have already been restored. On a new checkout, run `dotnet restore Atlas.slnx` first. Build and test sequentially so tests use a consistent set of compiled assemblies. Remove the filter to run the complete suite. A future full run includes the newly added tests, so its count need not match the original 586.

Local reports are retained under `artifacts/bootcamp-tests`: `bootcamp-final.trx` is the earlier full run with 17 failures despite its filename; `bootcamp-corrections.trx` is the subsequent 101-test passing run. These artifacts are ignored by Git. Preserve or use different filenames if you want to compare future runs without overwriting this local evidence.

For the manual startup check, follow the [first-session walkthrough](first-session.md). The observed fresh database returned one USD country, two clients to the platform administrator, one Acme client to the Acme viewer, and zero saved payroll runs both before and after a PH simulation. The smoke process was stopped after verification; its isolated database was retained.

## Practical limits

The implementation follows the repository's `EnsureCreated` development workflow. It creates the new tables in a fresh database; it does not migrate an older schema. The bootcamp launcher provides separate named databases for this reason. Production schema migration and deployment are separate work.

Earlier test attempts encountered a Windows Application Control assembly-loading failure. Later normal build/test runs executed successfully without changing that policy. The [journal](journal.md) keeps the earlier failure, its limits, and the later evidence instead of rewriting history.
