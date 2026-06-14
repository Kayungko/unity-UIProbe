# @run-ui-audit

How to perform visual audit of the project's UI by taking screenshots and comparing against requirements.

## Step 0 — UI Detection

1. Read `project.json` → `project_type` and `platforms`.
2. Determine if the project has a UI:
   - **Has UI**: `project_type` is one of `web-app`, `web-service`, `desktop-app`, `mobile-app`, or `platforms` includes `web`, `mobile`, `ios`, `android`, `desktop`.
   - **No UI**: `project_type` is `api-service`, `cli-tool`, or `library` → **skip this entire audit**, report "No UI detected — skipping visual audit."
3. Determine the screenshot strategy:
   - **Web project** (`project_type` contains "web" or `platforms` includes "web"): use Chrome DevTools MCP.
   - **Desktop application** (all other UI types): use system-level screenshot.

---

## Web Project Flow

### W1 — Ensure Dev Server Running

1. Check if the app is accessible: use `mcp__chrome-devtools__list_pages` to see if any page is already open at the project's URL.
2. If not accessible, read `project.json` → `dev_server_command` (or infer from package.json scripts: `dev`, `start`, `serve`).
3. Start the dev server in background using Bash with `run_in_background: true`.
4. Wait for the server to be ready (poll the URL for up to 30 seconds).
5. If the server cannot start, use `AskUserQuestion`:
   - "Dev server could not be started automatically. Is the app already running? If so, what URL should I navigate to?"
   - Options: "I'll start it manually — here's the URL", "Skip UI audit"

### W2 — Navigate and Capture Screenshots

1. **Graph-first UI module identification**: locate UI-related modules via the knowledge graph before reading full wiki pages:
   a. If `.claude/wiki/graph.json` exists: scan `god_nodes` for nodes with labels containing "ui", "page", "view", "route", or "screen"; use community membership to cluster related nodes.
   b. Read `.claude/wiki/modules/` for those specific module slugs (or all modules as fallback when graph.json is unavailable).
2. Read `.claude/tdd/specs/` to identify UI-related test scenarios with expected behaviors.
3. For each key page/route:
   a. Use `mcp__chrome-devtools__navigate_page` to visit the page.
   b. Use `mcp__chrome-devtools__wait_for` to wait for key content to appear.
   c. Use `mcp__chrome-devtools__take_screenshot` to capture the full page.
   d. If the page has interactive states (hover menus, modals, form states), capture those too.
4. Save screenshots to `.claude/progress/screenshots/` with descriptive names (e.g., `home-page.png`, `login-form.png`).

### W3 — Visual Review

1. Use the Read tool to view each screenshot (Claude is multimodal and can analyze images).
2. For each screenshot, check against wiki specifications and TDD scenarios:
   - **Layout correctness**: elements positioned as described in wiki/specs
   - **Text display**: correct content, no truncation, proper encoding (especially CJK characters)
   - **Responsive design**: if `platforms` includes mobile, also capture at mobile viewport using `mcp__chrome-devtools__emulate`
   - **Rendering anomalies**: blank areas, overlapping elements, broken images, unstyled content
   - **Functional appearance**: buttons, forms, navigation visible and styled
3. Record findings: for each issue found, note the screenshot file, the expected behavior (from wiki/TDD), and the actual observed behavior.

---

## Desktop Application Flow

### D1 — Ensure Application Window Visible

1. Use `AskUserQuestion` to confirm the application is running and its window is visible:
   - "The UI audit requires the application window to be visible on screen. Is the app running?"
   - Options: "Yes, it's open", "I'll open it now", "Skip UI audit"
2. If the user needs to open it: wait for confirmation.

### D2 — System-Level Screenshot

Detect the current OS and capture:

- **Windows (win32)**:
  ```powershell
  Add-Type -AssemblyName System.Windows.Forms
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bitmap = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
  $bitmap.Save("<repo-root>/.claude/progress/screenshots/desktop-ui.png")
  ```
- **macOS (darwin)**:
  ```bash
  screencapture -x <repo-root>/.claude/progress/screenshots/desktop-ui.png
  ```
- **Linux**:
  ```bash
  import -window root <repo-root>/.claude/progress/screenshots/desktop-ui.png
  # or: gnome-screenshot -f <path>
  ```

### D3 — Visual Review

Same as W3 but with desktop screenshots:
1. Use the Read tool to view the captured screenshot.
2. Check layout, text, controls, and visual consistency against wiki/TDD requirements.
3. If the screenshot shows the whole desktop (not just the app), focus analysis on the application window area.

---

## Step Final — Record Results

1. Produce a **UI Audit Report** section:
   ```
   ### UI Audit Results
   - Screenshots captured: N
   - Issues found: M
   - For each issue: [screenshot file] — expected vs actual, severity (high/medium/low)
   ```
2. Append results to session-log via @write-session-log.
3. If critical UI issues are found (broken layout, missing content, unreadable text):
   - Mark as HIGH severity findings in the sprint-review report.
   - These do NOT block the coverage gate, but are prominently reported for the user to decide.
