# AI Agent & Developer Handover Guide — Bulk Switch Backup Pro

> **Audience:** AI Coding Assistants (e.g. Antigravity, Claude, Gemini) and Software Engineers working on this codebase.  
> **Read this entire guide before making code modifications, proposing architectural changes, or executing git operations.**

---

## 1. Project Identity, Ownership & Attribution Rules

### 1.1 Strict Solo Project Ownership
- **Sole Developer & Author:** **Yash Nalawde** (`github.com/Majordean0303`, `linkedin.com/in/yash-nalawde`).
- **Zero Corporate Mentions:** This project is strictly independent. **Never** include, re-introduce, or commit any references to any legacy corporate entities or third-party company names anywhere in source code, documentation, EULA, plist, XML comments, git commit messages, or packaging scripts.
- **Verification:** Always run `.\scripts\audit-branding.ps1` before staging files. It scans the entire repository and fails with a non-zero exit code if any forbidden keywords or attribution mismatches are found.

### 1.2 Two-Repository Architecture
1. **Private Source Codebase (`Majordean0303/Bulk-Switch-Backup`)**:
   - Holds the complete source code, private git commit history, and GitHub Actions release workflows.
   - Active development, feature branches, and tag pushes happen here.
2. **Public Distribution Channel (`LazyAutomatar/Bulk-Switch-Backup-Pro`)**:
   - **Crucial Spelling Rule:** The organization name is **`LazyAutomatar`** (with an **`a`**), do not misspell with an 'o'.
   - Public-facing repository hosting production executable releases (`.exe`, `.zip`), public code signing certificates, and `update_manifest.json`.
   - Synchronized documentation: `README.md`, `EULA.txt`, `LICENSE.md`, `CHANGELOG.md`, and visual screenshots/logos are mirrored here automatically on every release.

---

## 2. UI Architecture & Design Language

### 2.1 Technology Stack & Core Frameworks
- **Framework:** **Avalonia UI 11.2.5** (Cross-platform for Windows, macOS, and Linux) with SkiaSharp rendering.
- **Controls & Styling:** **FluentAvaloniaUI 2.1.0** (faithful implementation of Windows 11 Fluent Design / WinUI 3 controls).
- **MVVM Architecture:** **CommunityToolkit.Mvvm 8.4.2** (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`).
- **Dependency Injection:** `Microsoft.Extensions.DependencyInjection` configured in `Program.cs`.

### 2.2 Dynamic Theming & Contrast Rules
- **Theme Switching:** Dark Mode and Light Mode are toggled via `MainWindowViewModel.ToggleThemeCommand` and applied using FluentAvalonia's `ThemeManager`.
- **Theme-Aware Dynamic Resources:**
  - Primary Text: `{DynamicResource TextFillColorPrimaryBrush}`
  - Secondary / Muted Text: `{DynamicResource TextFillColorSecondaryBrush}`
  - Accent Color: `{DynamicResource AccentFillColorDefaultBrush}` (cyan in dark mode, vibrant blue in light mode)
  - Card Backgrounds: `{DynamicResource CardBackgroundFillColorDefaultBrush}`
  - Subtle Pills / Inset Panels: `{DynamicResource SubtleFillColorSecondaryBrush}`
  - Card / Panel Borders: `{DynamicResource CardStrokeColorDefaultBrush}` or `{DynamicResource SurfaceStrokeColorDefaultBrush}`
- **Contrast Integrity Rule:** **Never** hardcode `Foreground="White"` or static hex colors on text blocks, pills, or badges. In Light Mode, white text on light grey backgrounds is invisible. Always use `{DynamicResource TextFillColorPrimaryBrush}` on a subtle pill with an accent border outline.
- **Monochrome Console Exception:** The live execution console explicitly maintains a dark terminal aesthetic (`Background="#181818"`, `Foreground="#D4D4D4"`).

### 2.3 Cross-Platform Title Bar Architecture
`MainWindow.axaml` uses FluentAvalonia window extension to render custom controls inside the title bar (`ExtendClientAreaToDecorationsHint="True"`, `ExtendClientAreaTitleBarHeightHint="38"`).
Window metrics are adjusted dynamically at runtime in `MainWindow.axaml.cs` via `UpdateTitleBarMetrics()`:
- **macOS Layout:** Native Apple traffic lights (Close, Minimize, Zoom) are positioned on the **top-left** (0..72px). `TitleBarLeadingContent` has a **`78px` left clearance** margin (`Thickness(78, 0, 0, 0)`).
- **Windows Layout:** Native caption buttons occupy the **top-right** (~138px). `TitleBarTrailingContent` has a **`142px` right gutter** margin (`Thickness(0, 0, 142, 0)`).
- **Uniform Action Pills:** All title bar pills share uniform metrics (height `28px`, corner radius `14px`).

---

## 3. Operational Discipline & Release Policy

### 3.1 CRITICAL: No Releases During Troubleshooting Iterations
- **NEVER create or push a release tag (`vX.Y.Z`) after every single fix or troubleshooting edit.**
- Tag pushes trigger the full cross-platform CI compiler and public GitHub release on `LazyAutomatar/Bulk-Switch-Backup-Pro`.
- **Workflow During Development/Fixes:**
  1. Make code edits.
  2. Verify with `.\scripts\test-all.ps1`.
  3. Commit to `main` without creating tags.
- **Workflow When Ready to Deploy a Release Milestone:**
  1. Run `.\scripts\prepare-release.ps1 -Version X.Y.Z`.
  2. Publish the coordinated release with `.\scripts\publish-release.ps1 -Version X.Y.Z -ConfirmPublish`.
