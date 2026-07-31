# Security Policy

## Supported versions

Security fixes are applied to the default branch of this repository.

## Reporting a vulnerability

Please report security issues privately, through
[GitHub Security Advisories](https://github.com/amaldini/maldalang/security/advisories/new).
That is the only private reporting channel for this project; there is no security mailing
address.

If advisories are unavailable to you for some reason, open a public issue that says only that
you have a security report and asks for a private channel — no details — and the maintainer
will open an advisory and invite you to it.

Include:

- A clear description of the issue
- Steps to reproduce
- Impact assessment (if known)
- Whether a fix or workaround is already known

Please do **not** open a public issue for undisclosed vulnerabilities.

## Scope notes

- Demo apps and examples may use weak default credentials for local learning. Never reuse those values in production.
- Treat API keys and LLM provider credentials as secrets; do not commit them.
