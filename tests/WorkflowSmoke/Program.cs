using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Microsoft.Agents.AI.Workflows;
using OpenAI;

// Deterministic API simulation: validates orchestration, not model answer quality.
var handler = new FakeGroq();
var client = new OpenAIClient(new ApiKeyCredential("test-only"), new OpenAIClientOptions
{
    Endpoint = new Uri("https://api.groq.com/openai/v1"),
    Transport = new HttpClientPipelineTransport(new HttpClient(handler))
}).GetChatClient("test-model");
var a = new TutorStage(client, "Understand", "Analyze");
var b = new TutorStage(client, "Solve", "Solve");
var c = new TutorStage(client, "Review", "Teach");
var workflow = new WorkflowBuilder(a).AddEdge(a, b).AddEdge(b, c).WithOutputFrom(c).Build();
await using var run = await InProcessExecution.RunStreamingAsync(workflow, "Target .NET 8: explain DI");
string? lesson = null;
await foreach (var evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent output) lesson = output.Data as string;
    if (evt is WorkflowErrorEvent) throw new Exception("Workflow error");
}
if (handler.Calls != 3 || lesson != "Reviewed lesson") throw new Exception("Incorrect workflow result");
Console.WriteLine("PASS: three ordered agent calls, preserved question and drafts, final reviewed lesson emitted.");

sealed class FakeGroq : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var body = await request.Content!.ReadAsStringAsync(token);
        Calls++;
        if (!body.Contains("Target .NET 8: explain DI")) throw new Exception("Lost original question");
        if (Calls >= 2 && !body.Contains("Analysis draft")) throw new Exception("Lost analysis");
        if (Calls == 3 && !body.Contains("Solution draft")) throw new Exception("Lost solution");
        var content = Calls switch { 1 => "Analysis draft", 2 => "Solution draft", 3 => "Reviewed lesson", _ => throw new Exception("Too many calls") };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"test","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"CONTENT"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                """.Replace("CONTENT", content), Encoding.UTF8, "application/json")
        };
    }
}
