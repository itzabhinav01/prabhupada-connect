# VedaBaseModern 2 — Corpus Ingestion & Book Pipeline Guide

This guide explains how to import and add new books into **VedaBaseModern 2** (`C:\VedaBaseModern2\Database\prabhupada_corpus.db`).

---

## 1. Book Ingestion Format (JSON)

Each book file is a standard JSON file containing metadata and an array of verse/paragraph records:

```json
{
  "bookKey": "TLC",
  "title": "Teachings of Lord Caitanya",
  "author": "A.C. Bhaktivedanta Swami Prabhupada",
  "language": "en",
  "records": [
    {
      "recordKey": "TLC-1",
      "reference": "TLC 1",
      "sequence": 10001,
      "devanagari": null,
      "transliteration": null,
      "synonyms": null,
      "translation": "The Supreme Personality of Godhead, Lord Caitanya...",
      "purports": "Full purport text here..."
    }
  ]
}
```

### Field Definitions:
- **`bookKey`** *(string, required)*: Unique uppercase identifier (e.g. `BG`, `SB`, `CC`, `NOD`, `ISO`, `NOI`).
- **`title`** *(string, required)*: Full display title (e.g. *Bhagavad-gītā As It Is*, *Śrīmad-Bhāgavatam*).
- **`author`** *(string, optional)*: Defaults to "A.C. Bhaktivedanta Swami Prabhupada".
- **`language`** *(string, optional)*: Defaults to "en".
- **`records`** *(array, required)*:
  - `recordKey`: Canonical unique verse key (e.g., `BG-1-1`, `SB-1-1-1`).
  - `reference`: Human-readable citation (e.g., `Bg 1.1`, `SB 1.1.1`).
  - `sequence`: Integer sequence for canonical ordering (auto-assigned if omitted).
  - `devanagari`: Sanskrit / Bengali original script (optional).
  - `transliteration`: Roman diacritic transliteration in IAST (optional).
  - `synonyms`: Word-for-word Sanskrit/Bengali to English glosses separated by semicolons (optional).
  - `translation`: Verse translation text.
  - `purports`: Purport commentary paragraphs.

---

## 2. CLI Commands

From PowerShell or Command Prompt:

### Import a Book:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- import-book C:\path\to\my_book.json
```

### List All Books in Corpus:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- list-books
```

### Validate Database Integrity:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- validate-corpus
```

### Rebuild FTS5 Search Index:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- rebuild-search-index
```

### Export Existing Book to JSON Template:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- export-book BG C:\VedaBaseModern2\tools\CorpusPipeline\bg_exported.json
```

---

## 3. PowerShell One-Click Helper (`add-book.ps1`)

```powershell
.\add-book.ps1 -Path ".\sample_book.json"
```
Automatically validates the file, imports it into SQLite, updates the full-text search index, and reports record counts.
