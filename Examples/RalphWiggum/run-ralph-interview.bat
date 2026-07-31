@echo off
setlocal EnableExtensions
cd /d "%~dp0..\.."

if "%MALDA_RALPH_WORKDIR%"=="" (
  echo MALDA_RALPH_WORKDIR is not set.
  echo Example:
  echo   set MALDA_RALPH_WORKDIR=C:\path\to\your\project
  echo   Examples\RalphWiggum\run-ralph-interview.bat
  exit /b 1
)

if "%OPENROUTER_API_KEY%"=="" (
  echo Note: OPENROUTER_API_KEY is not set. Configure ~/.malda/config.json or set the key.
)

set "MALDA_RALPH_INTERVIEW_MAX_LLM_ROUNDS=20"
set "MALDA_RALPH_INTERVIEW_FEWSHOT=%~dp0templates\PRD.fewshot.md"
echo Ralph Interview — workdir: %MALDA_RALPH_WORKDIR%
echo.
dotnet run --project "MaldaLang\MaldaLang.csproj" -- "Examples\RalphWiggum\RalphInterview.malda"
exit /b %ERRORLEVEL%
