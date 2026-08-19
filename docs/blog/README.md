# MALDA announcement article

The long-form announcement in [`docs/announcement.md`](../announcement.md) is
published automatically to **Telegraph** (not GitHub Pages):

<https://telegra.ph/MALDA-prompts-tools-and-agents-as-language-constructs-08-19>

`published.json` records the public URL and Telegraph path. The access token is
never committed.

## Publish or update

```bash
python3 scripts/publish-malda-article.py --check   # convert only
python3 scripts/publish-malda-article.py           # create or update
```

Updates require `TELEGRAPH_ACCESS_TOKEN` in the environment. On GitHub, that is
a repository Actions secret of the same name. The workflow
`.github/workflows/publish-article.yml` runs on changes to the announcement
copy and on `workflow_dispatch`. Without the secret it leaves the live page
alone instead of creating a duplicate.
