"""Read Atlas story plans, check real progress, practice recall, and run focused tests."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

from learn import dotnet_path

ROOT = Path(__file__).resolve().parents[1]
BOOTCAMP = ROOT / "astradocs" / "bootcamp"
VIEW_STORIES = {"MS-01", "MS-02", "MS-03", "MS-07", "MS-08", "MS-09", "MS-10", "MS-14", "MS-21", "MS-22", "MS-23", "MS-29"}
QUIZ = [
    ("Why does a background input read use a deferred SQLite transaction as well as WAL?", "WAL allows readers alongside a writer, but an immediate transaction can reserve the writer lock itself. A deferred read preserves its snapshot while another connection renews the job lease; the final fenced write uses a separate transaction."),
    ('Why allocate primitive money components before deriving department totals?', 'Independent rounding of derived totals can break each row’s net-pay and total-cost equations even when every column sums correctly.'),
    ('How is Unallocated different from not-captured?', 'Unallocated is a saved unsplit decision. Not-captured means no historical allocation rows exist for that payslip.'),
    ('Why does worker import commit repeat preview checks?', 'Preview does not reserve emails or countries. Current conflicts must be checked again, with database uniqueness and one transaction protecting races.'),
    ('Why does a committed import replay even after its validation expiry?', 'Expiry prevents acting on stale uncommitted validation. An already committed batch returns saved results rather than performing new work.'),
    ('What makes a workforce snapshot stable after a name change?', 'Capture copies labels into snapshot rows; later reads use those rows instead of joining live names.'),
    ('Why can two snapshots with the same asOf contain different names?', 'asOf controls employment membership; capturedAt controls when current names and titles were copied.'),
    ('How can an event failure roll back a salary save that already ran?', 'Both saves occur inside an explicit transaction. Only the outer commit makes them durable.'),
    ('How does a notification survive a process restart after approval?', 'Its source is a committed database event. A dispatcher can find missing notifications later, and uniqueness deduplicates competing inserts.'),
    ('Why can a viewer mark a notification read?', 'The marker belongs to that API identity and changes personal read state, not contract business state.'),
    ('Why recheck export access after submission?', 'The requester may have been revoked, expired, or lost management scope before processing or download.'),
    ('Why must download check expiry even when cleanup exists?', 'Cleanup can be delayed. The expiry boundary controls access independently of whether stored bytes have been cleared.'),
    ('Why begin document coverage with requirements rather than existing documents?', 'Otherwise an entirely absent required type can disappear from the result instead of producing a missing-type blocker.'),
    ('Why classify budget utilization before rounding its display percentage?', 'Rounding can move a value across a displayed threshold. Status must use exact underlying amounts.'),
    ('Does a custom field called salary change payroll?', 'No. Typed custom fields are metadata; financial behavior uses the contract and payroll calculation inputs explicitly.'),
    ("Why validate every batch item before completing the first?", "A later invalid member must not leave an earlier member completed. Check all membership, input, state and version rules before mutation, then save the batch atomically."),
    ("What must a stale-batch test check after the exception?", "Reload the other items through a fresh context and prove none of the losing batch persisted. An exception alone does not prove atomicity."),
    ("Why explicitly AddRange replacement children with fresh GUIDs?", "EF can infer existing-row updates when discovering nondefault generated keys under an existing tracked parent. Mark the replacement rows Added so persistence intent matches the object graph."),
    ("Why does editing only expense items touch the claim version?", "Approval is a decision about the entire claim. Without touching its parent token, an item-only edit could evade a concurrent approval's stale-data check."),
    ("Why do contracts copy onboarding template values?", "A template is live configuration; a checklist is the work attached to an individual contract. Updating the template must not silently rewrite existing checklists."),
    ("Why do hiring pause and contract creation share Country.Version?", "Creation must not commit using an unchanged-active assumption if a competing pause changed the country. Both writes coordinate on one parent version."),
    ("What is the difference between original billed, net billed and outstanding?", "Original billed is the saved invoice total. Net billed subtracts credits. Outstanding also subtracts net payments after reversals."),
    ("Why check a payment reference before remaining capacity?", "An exact replay is an existing operation, not another payment consuming balance. It returns the original row even if the first attempt used the remaining capacity."),
    ("Can two unique payment references still overdraw an invoice?", "Yes. Two writers can each read the same available balance. The invoice version must coordinate their inserts in addition to reference uniqueness."),
    ("Why can checking today's balance miss an invalid backdated payment?", "A later reversal may restore today's capacity while the proposed backdated payment makes an earlier balance negative. Validate every effective instant."),
    ("Which clock selects fees, and which selects invoice terms?", "The payroll run period selects the applicable fee schedule. The UTC issue day selects billing terms. The invoice snapshots both choices."),
    ("Does a successful leave quote reserve days?", "No. It explains one read of current inputs. Creation assesses again and saves a reservation only if the rules still pass."),
    ("Why do saved leave Days survive a later holiday change?", "Days records the reservation accepted at submission. Calendar changes affect new assessments; they do not silently recompute existing bookings."),
    ("Why do a final-pay preview and amendment preview read different salary sources?", "Final pay asks a payroll-period question and selects effective salary history. An amendment preview compares the current displayed Contract terms."),
    ("What keeps a first high-value expense approval out of payroll?", "The first vote leaves the claim Pending. Payroll selects explicitly Approved claims, and the second qualifying vote performs that transition."),
    ("Why reject expense edits after the first approval vote?", "Otherwise the reviewed amount could change under existing approval evidence. Before any vote, an edit replaces items and re-evaluates the policy snapshot."),
    ("Do two approver API-user IDs prove there are two humans?", "No. They establish two credential identities only. This application does not verify that separate people control them."),
    ("Why does a saved report reject an unknown filter?", "Silently ignoring a field can make the report answer a different question from the one the user intended. Typed allowed filters make the saved configuration explicit."),
    ("Why revalidate a saved report when running it?", "Stored configuration can be invalid or changed outside the API. Execution must still pass the supported kind and filter rules."),
    ("What happens to a preset when its API key is replaced?", "Replacement creates a new API-user identity. Preset ownership does not automatically transfer in this feature, even though the new credential copies the old role and client scope."),
    ("Why must client scope be applied before paging?", "Otherwise pages or counts may include another client's records. Ownership restricts the source query before optional filters and paging."),
    ("What does TryParse accept that IsDefined must still reject?", "Numeric values such as 999 can parse into an enum without naming any declared member. These endpoints also reject flags-style comma combinations."),
    ("Why check ASCII characters before normalization?", "Some non-ASCII characters can uppercase to ASCII. Checking only the normalized value can accept input outside the promised alphabet."),
    ("What are the three states of an optional Boolean filter?", "Omitted means no filter; true selects true records; false selects false records. False must not be mistaken for omission."),
    ("Why is a saved payslip different from a salary preview?", "The payslip preserves recorded outputs. A preview uses current selected inputs and may change when those inputs change."),
    ("Why do both worker ID and document ID belong in a nested detail query?", "The URL promises that this document belongs to this worker. An ID-only lookup ignores the parent relationship."),
    ("Why does quoting a CSV cell not solve spreadsheet formula injection?", "CSV quoting preserves field structure. Spreadsheet software can still interpret the cell's text as a formula, so formula-like text needs separate neutralization."),
    ("Why can an Ended benefit enrollment appear in an earlier month's calendar?", "Current status differs from historical date coverage. CoversMonth tests interval overlap, including the end date."),
    ("Why can one expense claim contribute to two months without its full total appearing twice?", "Items have incurred dates. Group item amounts by their own months instead of repeating the parent claim total."),
    ("Why does reading ready or pending not guarantee that the next write succeeds?", "Another request can change state after the read. The write must recheck authorization, state, and applicable concurrency tokens."),
]


def main():
    stories = json.loads((BOOTCAMP / "progress.json").read_text(encoding="utf-8"))
    by_id = {s["id"]: s for s in stories}
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("list", help="Show all story statuses without claiming unfinished work is done")
    for command in ("show", "test"):
        sub = commands.add_parser(command, help="Read one story" if command == "show" else "Run its current focused test suite")
        sub.add_argument("story", type=str.upper, choices=by_id)
    quiz = commands.add_parser("quiz", help="Practice recall before checking explanations")
    quiz.add_argument("--answers", action="store_true")
    serve = commands.add_parser("serve", help="Run the learning API with a separate SQLite database under artifacts/bootcamp")
    database = serve.add_mutually_exclusive_group()
    database.add_argument("--database", default="atlas-training-v1.db", help="Training database filename; existing files are preserved and reused")
    database.add_argument("--new", action="store_true", help="Create a uniquely named training database without deleting previous files")
    serve.add_argument("--port", type=int, default=5080)
    serve.add_argument("--dry-run", action="store_true", help="Show the launch command without starting the API")
    args = parser.parse_args()
    if args.command == "serve":
        if not 1024 <= args.port <= 65535:
            parser.error("port must be between 1024 and 65535")
        filename = f"atlas-training-{uuid.uuid4().hex}.db" if args.new else args.database
        if not re.fullmatch(r"[A-Za-z0-9_-]{1,80}\.db", filename):
            parser.error("database must be a .db filename using ASCII letters, digits, underscores or hyphens")
        dotnet = dotnet_path()
        if not dotnet:
            print(".NET SDK not found; use python scripts/learn.py doctor.", file=sys.stderr)
            return 1
        folder = ROOT / "artifacts" / "bootcamp"
        path = folder / filename
        command = [dotnet, "run", "--project", str(ROOT / "src" / "Atlas.Api"), "--no-launch-profile", "--",
                   "--urls", f"http://127.0.0.1:{args.port}", "--ConnectionStrings:Atlas", f"Data Source={path}"]
        print(f"Training database: {path}", flush=True)
        print("Existing databases are preserved. EnsureCreated does not upgrade older schemas.", flush=True)
        print("Running: " + subprocess.list2cmdline(command), flush=True)
        if args.dry_run:
            return 0
        folder.mkdir(parents=True, exist_ok=True)
        environment = os.environ.copy()
        environment["ASPNETCORE_ENVIRONMENT"] = "Development"
        environment["DOTNET_ENVIRONMENT"] = "Development"
        return subprocess.call(command, cwd=ROOT, env=environment)
    if args.command == "list":
        for s in stories:
            print(f"{s['id']}  {s['status']:12}  {s['title']}")
        return 0
    if args.command == "quiz":
        for index, (question, answer) in enumerate(QUIZ, 1):
            print(f"{index}. {question}")
            if args.answers:
                print(f"   {answer}")
        return 0
    story = by_id[args.story]
    print(f"Status: {story['status']}; evidence: {'; '.join(story['evidence']) or 'pending'}", flush=True)
    if args.command == "show":
        text = (ROOT / "astradocs" / story["workbook"]).read_text(encoding="utf-8")
        match = re.search(r"^## " + re.escape(args.story) + r":.*?(?=^## |\Z)", text, re.M | re.S)
        if not match:
            print("The story heading was not found; check the workbook link.", file=sys.stderr)
            return 1
        print(match.group(0))
        return 0
    suite = story.get("testSuite") or ("JuniorStoryTests" if args.story.startswith("JS-") else "MidlevelViewStoryTests" if args.story in VIEW_STORIES else None)
    if not suite:
        print("No focused suite is registered yet. This command does not pretend a planned story has tests.", file=sys.stderr)
        return 2
    dotnet = dotnet_path()
    if not dotnet:
        print(".NET SDK not found; use python scripts/learn.py doctor.", file=sys.stderr)
        return 1
    command = [dotnet, "test", str(ROOT / "tests" / "Atlas.Tests" / "Atlas.Tests.csproj"), "-c", "Release", "--nologo",
               "--filter", "|".join(f"FullyQualifiedName~{name}" for name in suite.split("|"))]
    print("Running: " + " ".join(command), flush=True)
    return subprocess.call(command, cwd=ROOT)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    raise SystemExit(main())
