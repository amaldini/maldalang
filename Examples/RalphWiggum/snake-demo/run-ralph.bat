@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Launch Ralph Wiggum for snake-demo in a dedicated git worktree (isolated from main WIP).
rem Requires OPENROUTER_API_KEY or providers.openrouter in ~/.malda/config.json
rem Worktree default: <parent-of-repo>\ralph-worktrees\snake-demo  branch: ralph/snake-demo

chcp 65001 >nul 2>nul
set "MALDA_AGENT_RICH=true"
set "MALDA_AGENT_VERBOSE=true"

set "SNAKE_DIR=%~dp0"
rem snake-demo -> RalphWiggum -> Examples -> repo root (3 levels up)
set "REPO_ROOT=%SNAKE_DIR%..\..\..\"
set "SCRIPTS_DIR=%SNAKE_DIR%..\scripts\"
set "PROJECT_REL=Examples/RalphWiggum/snake-demo"

pushd "%REPO_ROOT%" || (
    echo Could not find repo root: %REPO_ROOT%
    exit /b 1
)
for %%I in ("%CD%") do set "REPO_ROOT_FULL=%%~fI"

set "MALDA_RALPH_WORKDIR="
for /f "usebackq delims=" %%W in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPTS_DIR%Ensure-RalphWorktree.ps1" -RepoRoot "%CD%" -Name snake-demo -ProjectRelPath "%PROJECT_REL%"`) do set "MALDA_RALPH_WORKDIR=%%W"

if not defined MALDA_RALPH_WORKDIR (
    echo Failed to create or resolve Ralph worktree.
    popd
    exit /b 1
)

set "MALDA_RALPH_WORKTREE=%REPO_ROOT%..\ralph-worktrees\snake-demo"
for %%I in ("%MALDA_RALPH_WORKTREE%") do set "MALDA_RALPH_WORKTREE=%%~fI"
set "MALDA_RALPH_PROJECT_REL=%PROJECT_REL%"

set "MALDA_RALPH_PRD=PRD.md"
set "MALDA_RALPH_MAX_ITER=12"
set "MALDA_RALPH_MAX_LLM_ROUNDS=25"
rem OpenRouter: deepseek/deepseek-v4-flash (cheap) or deepseek/deepseek-v4-pro (stronger coding)
set "MALDA_RALPH_MODEL=deepseek/deepseek-v4-flash"
set "MALDA_RALPH_VALIDATE=true"
set "MALDA_RALPH_VALIDATE_DEPTH=recursive"
set "MALDA_RALPH_RESUME=true"
set "MALDA_RALPH_RESUME_POLICY=success-only"
set "MALDA_RALPH_INCLUDE_SYMBOLS=false"
set "MALDA_RALPH_RESET_EACH=auto"
set "MALDA_RALPH_PRD_STRICT=true"
set "MALDA_RALPH_REPORT=json"
set "MALDA_RALPH_PREFLIGHT=strict"
set "MALDA_RALPH_MAX_PHASE_RETRIES=3"
set "MALDA_RALPH_MAX_TOKENS=16384"
set "MALDA_RUN_COMMAND_POLICY=whitelist"
set "MALDA_RUN_COMMAND_DEFAULT_TIMEOUT_MS=120000"
set "MALDA_NON_INTERACTIVE=0"
set "MALDA_RALPH_GIT_COMMIT=true"
set "MALDA_RALPH_TASK=Goal: implement PRD.md in the workdir. Read PRD.md first. One [TODO] feature per iteration. Use list_directory (not powershell) to explore files. Use grep/read_file/edit_file/replace_in_file for file work. run_command only for dotnet/npm if needed. For git use git_status and git_add with files under this project only (snake.html PRD.md) - never run_command for git; never pass repoPath on git tools (defaults match the workdir). After write_file use git_status for untracked files - git_diff does not show new files. If snake.html already exists, use read_file + edit_file/replace_in_file (small snippets) - do NOT rewrite the whole file with write_file. write_file only for creating a new small file from scratch. Mark [DONE] only when validation passes."

if not defined MALDA_RALPH_QUIET (
    echo.
    echo Ralph Wiggum - Snake demo ^(git worktree^)
    echo Workdir:  %MALDA_RALPH_WORKDIR%
    echo Worktree: %MALDA_RALPH_WORKTREE%
    echo Branch:   ralph/snake-demo
    echo Model:    %MALDA_RALPH_MODEL%
    echo Main repo: %CD%
    echo.
)

dotnet run --project "MaldaLang\MaldaLang.csproj" -- "Examples\RalphWiggum\RalphWiggum.malda"
set "EXIT_CODE=!ERRORLEVEL!"

popd
echo.
if !EXIT_CODE! neq 0 (
    echo Ralph finished with errors, exit code !EXIT_CODE!.
) else (
    echo Ralph finished successfully.
    echo Merge when ready: git -C "%REPO_ROOT_FULL%" merge ralph/snake-demo
)
pause
exit /b !EXIT_CODE!

