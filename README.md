# CodeMentor

An ASP.NET Core 10 programming tutor using **Microsoft Agent Framework Workflows** (`Microsoft.Agents.AI.Workflows` 1.28.0) and Groq's OpenAI-compatible chat API. The app targets .NET 10; learners select .NET 8, 9 or 10 for their examples. It does not run generated code or guarantee answers to every question.

## Run

Install the .NET 10 SDK, then:

```bash
cd src/CodeMentor
# Supply Groq__ApiKey securely through your shell or deployment secret settings.
# Optional: Groq__Model (default: openai/gpt-oss-120b).
dotnet restore
dotnet run --urls http://127.0.0.1:5080
```

Open the address printed by ASP.NET Core in your local development environment. The API key stays on the server. Never commit it or paste it into a learning question. Questions and code are sent to Groq in three model calls per lesson. Check current Groq model availability for your account.

In this cloud instance the SDK is `/workspace/dotnet/dotnet`. Set `DOTNET_CLI_HOME=/workspace/dotnet-home` and `NUGET_PACKAGES=/workspace/nuget`. Use the existing checkout; tasks are isolated and do not need a new worktree.

## How it works

`Understand → Solve → Review` is an actual `WorkflowBuilder` graph with typed executors, executed by `InProcessExecution`. Each stage invokes a separate AI agent. The original question, framework and level travel through the graph alongside drafts. The review agent returns the final lesson with concepts, implementation, run instructions, tests, mistakes and an exercise.

The browser renders the answer as plain text so model output cannot execute HTML or scripts. Requests are validated, limited to 12,000 characters, limited to two concurrent lessons, and cancelled after two minutes or browser disconnection. Requests are independent: there is no saved chat history or database. The concurrency limit is not a billing quota.

## Verify

```bash
dotnet build src/CodeMentor
curl http://127.0.0.1:5080/api/status
curl -X POST http://127.0.0.1:5080/api/learn \
  -H 'Content-Type: application/json' \
  -d '{"question":"Explain dependency injection with a complete C# example","version":"10","level":"Beginner"}'
```

Without a key, status reports `configured: false` and a valid lesson request returns HTTP 503. Invalid inputs return HTTP 400. With a valid Groq key, verify that a lesson includes the requested framework and a complete solution. AI output is not compiler verification; run proposed examples and tests yourself.

This is a local development MVP. Before public deployment add authentication, per-user quotas, HTTPS enforcement, appropriate privacy/retention controls and observability. No generated-code execution, live documentation retrieval, persistence or production deployment is included.

Run the deterministic workflow smoke test with `dotnet run --project tests/WorkflowSmoke`. It simulates the Groq HTTP response and verifies the three calls, preservation of the original question and drafts, and emission of the final lesson. It does not verify live credentials or model quality.
