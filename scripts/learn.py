#!/usr/bin/env python3
"""Navigate Atlas lessons and run their real tests. Python 3.10+, no dependencies."""

import argparse
from pathlib import Path
import re
import shutil
import subprocess
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
LESSONS = {
    "00": ("first-session", "CountryEndpointTests"),
    "01": ("csharp-foundations", "PayrollCalculatorTests"),
    "02": ("http-and-authorization", "AuthorizationMatrixTests"),
    "03": ("domain-modeling", "LifecycleTransitionMatrixTests"),
    "04": ("sql-and-ef-core", "DateTimeOffsetConverterTests|ReportEndpointTests"),
    "05": ("testing-and-debugging", "ReportQueryTests"),
    "06": ("safe-refactoring", "ReportEndpointTests|ReportQueryTests"),
    "07": ("time-and-reporting", "ReportQueryTests"),
    "08": ("concurrency-and-reliability", "ConcurrencyEdgeTests|ProductionReadinessTests.ConcurrentUpdates"),
    "09": ("operations-and-diagnostics", "OperationalDiagnosticsTests|ProductionReadinessTests"),
    "10": ("design-and-delivery", "FullJourneyTests"),
}


def dotnet_path():
    """Support an SDK on PATH or the conventional user-local installation."""
    found = shutil.which("dotnet")
    if found:
        return found
    local = Path.home() / ".dotnet" / ("dotnet.exe" if sys.platform == "win32" else "dotnet")
    return str(local) if local.is_file() else None


def check_docs():
    """Check local inline Markdown link destinations, not web URLs or anchors."""
    failures = []
    documents = [ROOT / "README.md"]
    for directory in ("docs", "astradocs"):
        documents.extend(sorted((ROOT / directory).rglob("*.md")))
    for document in documents:
        # Fenced code examples may contain deliberately illustrative Markdown.
        content = re.sub(r"^```[^\n]*\n.*?^```\s*$", "", document.read_text(encoding="utf-8"),
                         flags=re.MULTILINE | re.DOTALL)
        for destination in re.findall(r"\[[^\]]*\]\(([^)]+)\)", content):
            target = destination.strip().strip("<>")
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            resolved = (document.parent / unquote(parsed.path)).resolve()
            if not resolved.exists():
                failures.append(f"{document.relative_to(ROOT)}: {destination}")
    if failures:
        print("Broken local links:\n" + "\n".join(failures), file=sys.stderr)
        return 1
    print(f"Checked local inline links in {len(documents)} Markdown files (URLs and anchors not checked).")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("list", help="List lessons and their focused test suites")
    commands.add_parser("doctor", help="Locate the SDK and display installed SDK versions")
    commands.add_parser("check-docs", help="Check local inline Markdown link targets")
    show = commands.add_parser("show", help="Print a lesson without leaving the terminal")
    show.add_argument("lesson", choices=LESSONS)
    test = commands.add_parser("test", help="Run actual .NET tests associated with a lesson")
    test.add_argument("lesson", choices=[*LESSONS, "all"])
    args = parser.parse_args()

    if args.command == "list":
        for number, (slug, suites) in LESSONS.items():
            print(f"{number}  {slug.replace('-', ' ')}\n    Tests: {suites}")
        return 0
    if args.command == "show":
        path = ROOT / "docs" / "learning" / f"{args.lesson}-{LESSONS[args.lesson][0]}.md"
        print(path.read_text(encoding="utf-8"))
        return 0
    if args.command == "check-docs":
        return check_docs()

    dotnet = dotnet_path()
    if not dotnet:
        print(".NET SDK not found. Install .NET 10 and add it to PATH; see docs/getting-started.md.", file=sys.stderr)
        return 1
    if args.command == "doctor":
        print(f"Repository: {ROOT}\nSDK executable: {dotnet}", flush=True)
        return subprocess.call([dotnet, "--list-sdks"], cwd=ROOT)

    command = [dotnet, "test", str(ROOT / "Atlas.slnx"), "--nologo"]
    if args.lesson != "all":
        suites = LESSONS[args.lesson][1].split("|")
        command += ["--filter", "|".join(f"FullyQualifiedName~{suite}" for suite in suites)]
    print("Running: " + " ".join(command), flush=True)
    # Argument lists avoid shell interpolation; preserve dotnet's failure exit code.
    return subprocess.call(command, cwd=ROOT)


if __name__ == "__main__":
    # Windows pipes can default to a legacy code page that cannot print lesson
    # arrows and other Unicode text. Keep terminal and redirected output UTF-8.
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    raise SystemExit(main())
