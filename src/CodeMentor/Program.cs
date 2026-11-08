using Microsoft.AspNetCore.RateLimiting;
using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(o => o.AddConcurrencyLimiter("tutor", l => { l.PermitLimit = 2; l.QueueLimit = 0; }));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/api/status", () => new { configured = !string.IsNullOrWhiteSpace(builder.Configuration["Groq:ApiKey"]), provider = "Groq" });
app.MapPost("/api/learn", async (LearnRequest request, HttpContext http, ILoggerFactory logs) =>
{
    if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 12000 || request.Version is not ("8" or "9" or "10") || request.Level is not ("Beginner" or "Intermediate" or "Advanced"))
        return Results.BadRequest(new { error = "Enter a question (up to 12,000 characters), .NET version 8–10, and a valid experience level." });
    var key = builder.Configuration["Groq:ApiKey"];
    if (string.IsNullOrWhiteSpace(key)) return Results.Json(new { error = "Set Groq__ApiKey on the server." }, statusCode: 503);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));
    try
    {
        var client = new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") }).GetChatClient(builder.Configuration["Groq:Model"] ?? "openai/gpt-oss-120b");
        var understand = new TutorStage(client, "Understand", "Restate the question. Identify inputs, expected output, constraints, prerequisites and likely causes. State assumptions and ask specific questions when essential details are missing.");
        var solve = new TutorStage(client, "Solve", "Use the question and analysis to teach a practical solution. Give steps, complete C# examples with usings, project creation and run commands, expected output, edge cases and tests. Honor the requested .NET version and distinguish version-specific APIs. For debugging explain cause, diagnostic steps and fix.");
        var review = new TutorStage(client, "Review", "Review and correct the drafts for accuracy, .NET compatibility and teaching clarity. Return a self-contained lesson with sections: Understanding the question, Key concepts, Plan, C# solution, How to run, How to test, Why it works, Common mistakes, Practice exercise. State assumptions and missing details. Never claim code was executed.");
        var workflow = new WorkflowBuilder(understand).AddEdge(understand, solve).AddEdge(solve, review).WithOutputFrom(review).Build();
        var input = $"Target: .NET {request.Version}; Experience: {request.Level}\nLearner question:\n{request.Question}";
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, input, cancellationToken: timeout.Token);
        string? lesson = null;
        await foreach (var evt in run.WatchStreamAsync(cancellationToken: timeout.Token))
        {
            if (evt is WorkflowOutputEvent output) lesson = output.Data as string;
            if (evt is WorkflowErrorEvent) throw new InvalidOperationException("Workflow failed.");
        }
        return lesson is null ? Results.Problem("The workflow returned no lesson.") : Results.Ok(new { lesson });
    }
    catch (OperationCanceledException) { return Results.Json(new { error = "Request cancelled or timed out. Try a smaller question." }, statusCode: 504); }
    catch (Exception ex)
    {
        logs.CreateLogger("Tutor").LogWarning("Tutor failed: {Type}", ex.GetType().Name);
        return Results.Json(new { error = "Groq could not complete the lesson. Check the server API key, model and provider limits." }, statusCode: 502);
    }
}).RequireRateLimiting("tutor");
app.Run();
record LearnRequest(string Question, string Version, string Level);
