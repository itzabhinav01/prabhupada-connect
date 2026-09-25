# VedaBaseModern 2 — Guide to Adding New Books in the Future

This document provides a complete guide for adding new scriptures, biographies, or devotional books into **VedaBaseModern 2**.

---

## 1. Corpus Architecture Overview

All literature in VedaBaseModern resides in an offline SQLite database:
`C:\VedaBaseModern2\Database\prabhupada_corpus.db`

The database contains two core tables and one virtual full-text search index:
1. **`Books`**: Metadata for each book (`BookKey`, `Title`, `Author`, `Category`, `CanonicalOrder`, `TotalRecords`).
2. **`Records`**: Every individual verse or chapter record (`RecordKey`, `BookKey`, `Sequence`, `Reference`, `Title`, `Devanagari`, `Transliteration`, `Synonyms`, `Translation`, `Purports`).
3. **`SearchIndex`** (FTS5): High-speed full-text search index across all verses and commentaries.

---

## 2. Ingestion Methods

There are two primary methods to import books depending on your source file format:

### Method A: One-Click JSON Import (Recommended for Structured Text / Markdown)

Use the built-in `CorpusPipeline` tool located at `C:\VedaBaseModern2\tools\CorpusPipeline\`.

#### Step 1: Create a JSON file for your book (`my_book.json`)

```json
{
  "bookKey": "MYSCRIPTURE",
  "title": "Title of the Book",
  "author": "Author Name",
  "category": "Scripture",
  "language": "en",
  "records": [
    {
      "recordKey": "MYSCRIPTURE-1-1",
      "reference": "MyScripture 1.1",
      "sequence": 1,
      "title": "Text 1",
      "devanagari": "अथ वा बहुनैतेन...",
      "transliteration": "atha vā bahunaitena...",
      "synonyms": "atha vā — or; bahunā — much; etena — this...",
      "translation": "English translation here...",
      "purports": "Full purport commentary text with paragraphs separated by newlines."
    }
  ]
}
```

*For narrative prose books without Sanskrit verses (such as biographies, essays, or memoirs), leave `devanagari`, `transliteration`, and `synonyms` as `null`.*

#### Step 2: Run the automated importer

Open PowerShell and run:
```powershell
# Quick PowerShell script
cd C:\VedaBaseModern2\tools\CorpusPipeline
.\add-book.ps1 -Path "C:\path\to\my_book.json"
```

Or via direct .NET CLI:
```powershell
dotnet run --project C:\VedaBaseModern2\tools\CorpusPipeline\CorpusPipeline.csproj -- import-book "C:\path\to\my_book.json"
```

The pipeline automatically:
- Inserts or updates the book entry in `Books`.
- Inserts each record into `Records`.
- Synchronizes the FTS5 `SearchIndex`.
- Validates sequence ordering and integrity.

---

### Method B: Custom Python Script (Recommended for RTF / Complex Formats)

If you have an RTF file (such as a Folio Views export), follow the model established in [`C:\VedaBaseModern2\tools\ingest_lilamrit.py`](file:///C:/VedaBaseModern2/tools/ingest_lilamrit.py):

1. **Place source file**: Put your `.rtf` in `C:\VedaBaseModern2\Database\sources\`.
2. **Decode Diacritics**: Use the standard Balaram/Tamal font decoding dictionary (e.g. `\'e4` -> `ā`, `\'e5` -> `ṛ`, `\'e7` -> `ś`, `\'e9` -> `ī`, `\'eb` -> `ṇ`, etc.).
3. **Insert Records**: Use SQLite `INSERT OR REPLACE INTO Records (...)` and `INSERT OR REPLACE INTO SearchIndex (...)`.
4. **Run Ingestion**:
   ```powershell
   python C:\VedaBaseModern2\tools\ingest_lilamrit.py
   ```

---

## 3. Registering the Book in the User Interface

To make the new book appear in the Library hierarchy and search filters:

1. **Open** [`BookRegistry.cs`](file:///C:/VedaBaseModern2/App/VedaBaseModern2.Core/Registry/BookRegistry.cs):
   Add your book descriptor to `CanonicalPrabhupadaBooks`:
   ```csharp
   new BookDescriptor { 
       BookKey = "MYSCRIPTURE", 
       Title = "Title of the Book", 
       Author = "Author Name", 
       Abbreviation = "MS", 
       Category = "Scripture", // or "Biographies", "Philosophy", "Essays & Articles"
       CanonicalOrder = 47 
   }
   ```

2. **Open** [`SqliteCorpusRepository.cs`](file:///C:/VedaBaseModern2/App/VedaBaseModern2.Core/Repositories/SqliteCorpusRepository.cs):
   Add the book key to `BookTitles` dictionary and `CanonicalBookOrder` list:
   ```csharp
   BookTitles.Add("MYSCRIPTURE", "Title of the Book");
   CanonicalBookOrder.Add("MYSCRIPTURE");
   ```

3. **Rebuild the Application**:
   ```powershell
   dotnet build C:\VedaBaseModern2\VedaBaseModern2.sln -c Release
   ```

---

## 4. Useful Pipeline Utility Commands

From `C:\VedaBaseModern2\tools\CorpusPipeline\`:

- **List all books and verse counts in database**:
  ```powershell
  dotnet run -- list-books
  ```
- **Validate corpus integrity (missing translations, sequence gaps)**:
  ```powershell
  dotnet run -- validate-corpus
  ```
- **Rebuild FTS5 Search Index**:
  ```powershell
  dotnet run -- rebuild-search-index
  ```
- **Export an existing book to JSON template**:
  ```powershell
  dotnet run -- export-book BG C:\temp\bg_template.json
  ```
