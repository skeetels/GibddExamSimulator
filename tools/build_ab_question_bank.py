#!/usr/bin/env python3
"""Build and validate the canonical A/B/M-only question bank.

The source snapshot is the verified 1.2.0 combined JSON and JPEG tree.  This
script never downloads data and never changes question wording or answers.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


EXPECTED_QUESTIONS = 800
EXPECTED_TICKETS = 40
EXPECTED_BLOCKS = 160
EXPECTED_IMAGES = 548


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def validate_jpeg(path: Path) -> None:
    payload = path.read_bytes()
    if len(payload) < 4 or payload[:2] != b"\xff\xd8" or payload[-2:] != b"\xff\xd9":
        raise ValueError(f"Not a baseline-compatible JPEG: {path}")
    if payload[:4] == b"RIFF" or b"WEBP" in payload[:16]:
        raise ValueError(f"WebP payload is not allowed: {path}")


def validate_questions(questions: list[dict]) -> set[str]:
    if len(questions) != EXPECTED_QUESTIONS:
        raise ValueError(f"Expected {EXPECTED_QUESTIONS} AB questions, found {len(questions)}")

    ids = [int(question["id"]) for question in questions]
    if sorted(ids) != list(range(1, EXPECTED_QUESTIONS + 1)):
        raise ValueError("AB question IDs must be exactly 1..800")
    if any(str(question.get("category", "")).upper() != "AB" for question in questions):
        raise ValueError("Canonical bank contains a non-AB category")

    tickets = Counter(int(question["ticket"]) for question in questions)
    if sorted(tickets) != list(range(1, EXPECTED_TICKETS + 1)) or any(count != 20 for count in tickets.values()):
        raise ValueError("Canonical bank must contain 40 tickets with 20 questions each")

    blocks = Counter((int(question["group"]), int(question["thematicBlock"])) for question in questions)
    if len(blocks) != EXPECTED_BLOCKS or any(count != 5 for count in blocks.values()):
        raise ValueError("Canonical bank must contain 160 thematic blocks with 5 questions each")

    image_paths = {str(question["image"]) for question in questions if question.get("image")}
    if len(image_paths) != EXPECTED_IMAGES:
        raise ValueError(f"Expected {EXPECTED_IMAGES} referenced AB images, found {len(image_paths)}")
    if any("cd" in image_path.lower().replace("\\", "/").split("/") for image_path in image_paths):
        raise ValueError("Canonical bank references a CD image")
    return image_paths


def normalized_ab_question(question: dict) -> dict:
    result = dict(question)
    image = result.get("image")
    if image:
        file_name = Path(str(image).replace("\\", "/")).name
        result["image"] = f"images/{file_name}"
    result["category"] = "AB"
    return result


def validate_output(output_root: Path) -> None:
    questions_path = output_root / "official-questions.json"
    manifest_path = output_root / "bank-manifest.json"
    if not questions_path.is_file() or not manifest_path.is_file():
        raise FileNotFoundError("Canonical bank JSON or manifest is missing")

    document = json.loads(questions_path.read_text(encoding="utf-8"))
    questions = document["questions"]
    image_paths = validate_questions(questions)
    actual_files = {f"images/{path.name}" for path in (output_root / "images").glob("*.jpg")}
    if actual_files != image_paths:
        missing = sorted(image_paths - actual_files)
        extra = sorted(actual_files - image_paths)
        raise ValueError(f"Image set mismatch. Missing={missing[:5]}, extra={extra[:5]}")
    for relative_path in sorted(image_paths):
        validate_jpeg(output_root / relative_path)

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("bankSha256") != sha256_file(questions_path):
        raise ValueError("bankSha256 does not match official-questions.json")
    expected = {
        "questionCount": EXPECTED_QUESTIONS,
        "ticketCount": EXPECTED_TICKETS,
        "blockCount": EXPECTED_BLOCKS,
        "imageCount": EXPECTED_IMAGES,
        "imageBytes": sum((output_root / path).stat().st_size for path in image_paths),
    }
    for key, value in expected.items():
        if manifest.get(key) != value:
            raise ValueError(f"Manifest {key} must be {value}")


def build(source_json: Path, source_images: Path, output_root: Path) -> None:
    source = json.loads(source_json.read_text(encoding="utf-8-sig"))
    source_questions = source["questions"] if isinstance(source, dict) else source
    questions = [normalized_ab_question(question) for question in source_questions
                 if str(question.get("category", "")).upper() == "AB"]
    questions.sort(key=lambda question: int(question["id"]))
    image_paths = validate_questions(questions)

    output_root.mkdir(parents=True, exist_ok=True)
    image_root = output_root / "images"
    image_root.mkdir(parents=True, exist_ok=True)
    for relative_path in sorted(image_paths):
        source_image = source_images / Path(relative_path).name
        if not source_image.is_file():
            raise FileNotFoundError(f"Source image is missing: {source_image}")
        validate_jpeg(source_image)
        shutil.copy2(source_image, output_root / relative_path)

    metadata = dict(source.get("metadata", {})) if isinstance(source, dict) else {}
    metadata["title"] = "Экзаменационные билеты A/B/M, опубликованные Госавтоинспекцией России"
    metadata["sourcePages"] = ["https://xn--90adear.xn--p1ai/mens/avtovladeltsam/abm"]
    metadata["validation"] = {
        "AB": {
            "questions": EXPECTED_QUESTIONS,
            "tickets": EXPECTED_TICKETS,
            "blocks": EXPECTED_BLOCKS,
            "images": EXPECTED_IMAGES,
        }
    }
    metadata["categories"] = ["AB"]
    metadata["questionCount"] = EXPECTED_QUESTIONS
    document = {"metadata": metadata, "questions": questions}
    questions_path = output_root / "official-questions.json"
    questions_path.write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    snapshot = metadata.get("snapshot", {})
    manifest = {
        "schemaVersion": 1,
        "bankVersion": str(snapshot.get("description") or "2026.Q2.0") + "-ab",
        "bankSha256": sha256_file(questions_path),
        "questionCount": EXPECTED_QUESTIONS,
        "ticketCount": EXPECTED_TICKETS,
        "blockCount": EXPECTED_BLOCKS,
        "imageCount": EXPECTED_IMAGES,
        "imageBytes": sum(path.stat().st_size for path in image_root.glob("*.jpg")),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "sourceGeneratedAtUtc": metadata.get("generatedAtUtc"),
        "sources": ["https://xn--90adear.xn--p1ai/mens/avtovladeltsam/abm"],
        "transportSnapshot": snapshot,
    }
    (output_root / "bank-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    validate_output(output_root)


def main() -> None:
    repository = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-json", type=Path)
    parser.add_argument("--source-images", type=Path)
    parser.add_argument("--output", type=Path, default=repository / "assets/question-bank/ab")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    if args.validate_only or args.source_json is None:
        validate_output(args.output.resolve())
    else:
        if args.source_images is None:
            parser.error("--source-images is required when --source-json is supplied")
        build(args.source_json.resolve(), args.source_images.resolve(), args.output.resolve())
    print("AB question bank validation: OK")


if __name__ == "__main__":
    main()
