# Contributing to Prabhupāda Connect

Hare Krishna! Thank you for your interest in contributing to **Prabhupāda Connect** — an open-source, high-performance research ecosystem dedicated to preserving and studying the complete teachings of His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda.

---

## 🛠️ Tech Stack & Prerequisites

- **Language / Runtime**: C# 13 / .NET 10 (`net10.0-windows10.0.26100.0`)
- **Desktop UI**: Windows App SDK (WinUI 3) + CommunityToolkit.Mvvm
- **Reader Engine**: Microsoft WebView2 with custom HTML5/CSS3/ES6 high-performance rendering bridge (`reader.html`, `reader.css`, `reader.js`)
- **Database & Search**: SQLite 3 with FTS5 Full-Text Search, BM25 ranking, reverse concordance lemmatization, and relational schema
- **Corpus Tools**: Python 3.10+ / C# CLI for pipeline extraction and repair
- **Testing**: xUnit / .NET test runner (`tests/VedaBaseModern2.Tests`)

---

## 🚀 Getting Started

1. **Clone the repository with Git LFS**:
   ```bash
   git clone https://github.com/itzabhinav01/prabhupada-connect.git
   cd prabhupada-connect
   git lfs pull
   ```

2. **Verify .NET 10 SDK & Workloads**:
   Ensure you have .NET 10 SDK and the Windows App SDK workload installed:
   ```bash
   dotnet --version
   ```

3. **Build the Solution**:
   ```bash
   dotnet build App/VedaBaseModern2.UI/VedaBaseModern2.UI.csproj -c Release
   ```

4. **Run the Test Suite**:
   ```bash
   dotnet run --project tests/VedaBaseModern2.Tests/VedaBaseModern2.Tests.csproj
   ```

5. **Launch the Application**:
   Run via Visual Studio 2022/2025, or execute:
   ```powershell
   ./Launch.ps1
   ```

---

## 🏛️ Codebase Architecture

The project is structured into clean, decoupled layers:

- `App/VedaBaseModern2.Core/`:
  - **Models**: `CorpusRecord`, `SearchResult`, `BookDescriptor`, `ConcordanceResult`, etc.
  - **Repositories**: `SqliteCorpusRepository` (pure canonical sorting, direct SQLite pagination, FTS5 BM25 relevance ranking, vocabulary terms, duplicate filtering), `SqliteUserRepository` (bookmarks, highlights, notes).
  - **Services**: `UnifiedSearchService`, `DirectReferenceService` (instant `@BG 4.9`, `@SB 1.1.1` in-memory resolver), `ConcordanceService`.
  - **Registry**: `BookRegistry`, `CorpusRegistry`.

- `App/VedaBaseModern2.UI/`:
  - **Views**: `MainPage`, `ReadingPage` (hybrid WinUI 3 + WebView2 bridge), `SearchPage` (FTS search with canonical and relevance sorting), `LibraryPage`, `SettingsPage`, `AdvancedSearchDialog`.
  - **ViewModels**: CommunityToolkit.Mvvm view models (`SearchViewModel`, `ReadingViewModel`, `MainViewModel`, `NotesViewModel`, `SettingsViewModel`).
  - **Assets/Reader/**: 60fps CSS GPU accelerated reader, interactive Sanskrit lemma word inspection, prosody & meter badge detector, audio chanting synthesizer.

- `Database/`:
  - `prabhupada_corpus.db`: Complete SQLite database with UTF-8 diacritics, Devanagari, transliteration, word-for-word synonyms, translations, purports, and FTS5 index.

- `tests/`:
  - Automated unit and regression test suite verifying all 134+ critical invariant checks.

---

## 📏 Contributing Guidelines

1. **MVVM Integrity**: Keep UI markup in XAML/CSS, presentation logic in ViewModels, and data access in Repositories/Services.
2. **Canonical Scripture Preservation**: Never alter or normalize authentic Sanskrit diacritics (IAST) or Devanagari texts in the corpus database without verified scholarly cross-references.
3. **Performance First**: Ensure search and reading interactions remain sub-100ms. Keep virtualized lists virtualized.
4. **Run Tests**: Always run `dotnet run --project tests/VedaBaseModern2.Tests/VedaBaseModern2.Tests.csproj` before opening a pull request to ensure all tests pass.

---

## 📱 Mobile Vision (Android / Cross-Platform)

We are actively designing a companion Android app for Prabhupāda Connect. If you have experience with:
- Kotlin / Jetpack Compose
- Kotlin Multiplatform (KMP)
- SQLite / Room with FTS5
- High-performance text rendering with Sanskrit fonts

Please feel free to open an issue or discussion on GitHub!

All devotees, researchers, and developers are welcome! 🙏
