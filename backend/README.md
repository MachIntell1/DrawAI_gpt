# MachIntell deterministic drawing backend

This package plans and validates SolidWorks manufacturing drawings from a
feature manifest.  It does not ask an LLM to invent dimensions.  Controlling
requirements are derived from native feature metadata, persistent model
references, topology evidence, and explicit engineering intent.

The service can always produce a reviewable draft.  It reports
`RELEASE_READY` only when deterministic geometry coverage, associative
execution, standards consistency, title/configuration data, engineering intent,
and recorded human approval all pass.

## Run

```bash
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
.venv\Scripts\uvicorn app.main:app --host 0.0.0.0 --port 8000
```

On Linux/macOS, use `.venv/bin/...` instead.  Copy `.env.example` to `.env` if
you need non-default settings.  Never place a production API key in source.

## API

- `GET /health`
- `GET /api/v2/standards/profiles`
- `POST /api/v2/plugin/plan`
- `POST /api/v2/plugin/validate-execution`
- Compatibility aliases remain at `/api/v1/plugin/...`.

Set `API_KEY` in production; clients then authenticate with `X-API-Key`. The
execution endpoint requires the server-cached immutable plan, so a client
cannot edit a plan's release blockers before validation. Replace the in-memory
repository with a durable adapter before multi-instance production deployment.

See `docs/CONTRACT.md`, `docs/STANDARDS.md`, and the executable fixtures in
`tests/fixtures.py`.

## Safety boundary

The backend never infers numeric tolerances, datum systems, fits, thread
classes, surface texture, heat treatment, coatings, or approval.  Company
defaults may be applied only when the request identifies an approved,
versioned policy.  Missing design intent is returned as a release blocker.
