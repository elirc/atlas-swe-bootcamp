"""Render the readable story register from its explicit evidence records."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "astradocs" / "bootcamp"


def main():
    stories = json.loads((FOLDER / "progress.json").read_text(encoding="utf-8"))
    allowed = {"planned", "in-progress", "implemented", "verified"}
    assert len(stories) == 75 and len({s["id"] for s in stories}) == 75
    for story in stories:
        assert story["status"] in allowed
        if story["status"] == "verified":
            assert story["evidence"], f"{story['id']} needs verification evidence"
    counts = {status: sum(s["status"] == status for s in stories) for status in sorted(allowed)}
    lines = ["# Story implementation progress", "",
             "All 75 stories remain in scope. This register records actual implementation and verification, not promises.", "",
             " | ".join(f"{status}: {count}" for status, count in counts.items()), "",
             "A story marked implemented still needs its focused verification. Verified stories include evidence below.", "",
             "See the [verification record](verification.md) for full-run failures, the passing correction run, exact commands, and practical limits.", "",
             "| ID | Story | Status | Evidence |", "| --- | --- | --- | --- |"]
    for story in stories:
        evidence = "; ".join(story["evidence"]) or "Pending"
        lines.append(f"| {story['id']} | {story['title']} | {story['status']} | {evidence.replace('|', '/')} |")
    (FOLDER / "progress.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Rendered 75 stories: {counts}")


if __name__ == "__main__":
    main()
