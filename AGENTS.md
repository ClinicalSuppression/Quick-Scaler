# Agent Guidelines & Workflow Rules

## 1. Stable Release Checkpointing
- Do not commit, tag, push, or create releases on every incremental edit.
- **Only** execute the formal release workflow (git commit, semantic version tag, push to GitHub, and GitHub release) when reaching major, stable release milestones.

## 2. Standardized Release Assets
Every major stable release must include:
- **Source Zip**: A normalized source archive (`<repo-name>-vX.Y.Z-src.zip`).
- **Terminal Installation Instructions**: Clear instructions in `README.md` for terminal/CLI installation (e.g., `git clone` into plugin directories).
- **Executables (`.exe`)**: If the app compiles or builds an `.exe`, include both:
  1. A portable executable package.
  2. An installation candidate (`.exe` installer / setup executable).
  - *Note*: If a portable version is unfeasible, notify the user and ask for direction before publishing.
