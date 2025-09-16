**About the project**

This is a simple web service in C# .NET 8 for running requests on HTTP or PowerShell (for Exchange Online stuff). Clients send requests to /api/something, and it routes to the right executor, adds retries if fails, logs everything, and gives back a nice JSON response with details like time taken and status. It's made for production, but kept simple.

**High level sequence**

Client sends POST/GET to /api/path  -->   App checks path: if powershell/ then PS, else HTTP  -->  Validate auth and body, then retry up to 3 times if error  --> Run command or HTTP call, get result --> Send back JSON with request ID, status, attempts

**Design decisions**

1. API route: /api/{**path} for all methods. If path starts with "powershell/", it's PS command. Else, it's HTTP URL.
2. Response: Always JSON with requestId, executorType, timestamps, status, attempts list, and result.
3. Retry: 3 tries max, exponential delay with jitter for network issues or 5xx errors. Non-retry for 4xx or auth fail.
4. PowerShell: Only Get-Mailbox and Get-User allowed. Per request connect/disconnect for safety, no sharing sessions.
5. Metrics: Simple in-memory counters for requests, success, fail, retry, avg and p95 latency.
6. Logging: JSON format, masks passwords and tokens.

**Security notes**
Body limit to stop big payloads. PS only safe commands. Logs hide secrets like pass=***. No secrets in code. Limit: Basic auth via headers, not best, but simple.

**How to run locally**

1. Open terminal in root folder.
2. Go to Src: cd Src
3. Restore: dotnet restore
4. Build: dotnet build
5. Run: dotnet run

App runs on http://localhost:7281 or whatever port it says.

Test ping: curl http://localhost:7281/ping  (should say pong)

Test HTTP: curl http://localhost:7281/api/https://jsonplaceholder.typicode.com/todos/1

Test PS: curl -X POST http://localhost:7281/api/powershell/Get-Mailbox -H "X-Auth-User: user@ex.com" -H "X-Auth-Pass: pass" -d '{"Identity":"john"}'

For metrics: curl http://localhost:7281/metrics


**Docker Support**

Dockerfile in root. Build: docker build -t rres .

Run: docker run -d -p 8080:80 rres

Test same as local but localhost:8080.


**Testing**

Tests in Tests folder. Run: dotnet test

Units for validation, retry, response. Integration for HTTP with mock server.

Gaps: PS tests mock only, no real EXO.

**Test matrix:**

Bad path	HTTP	400	No	0	Error message
Bad PS cmd	PS	400	No	0	Invalid command
HTTP good	HTTP	200	No	0	Success status
HTTP 500 retry	HTTP	200	Yes	1	Attempts 2
PS auth bad	PS	500	No	0	Fail no retry


**Examples**

**For http**
examples/http_request.json:
{
"note": "For GET https://jsonplaceholder.typicode.com/todos/1"
}

examples/http_response.json:
{
"RequestId": "abc123",
"ExecutorType": "http",
"Start": "2025-09-16T00:00:00Z",
"End": "2025-09-16T00:00:01Z",
"Status": "Success",
"Attempts": [{"Attempt":1,"Outcome":"Success","DurationMs":100}],
"Result": {"Status":200,"Body":"{"title":"test"}"}
}

**For PowerShell**

PS C:\WINDOWS\system32> Invoke-RestMethod -Method Post -Uri "https://localhost:7281/api/powershell/Get-Mailbox" -Headers @{ "X-Auth-User" = "user@ex.com"; "X-Auth-Pass" = "pass" } -Body (@{ Identity = "john" } | ConvertTo-Json) -ContentType "application/json"


requestId    : 1fa8a2b1-e45c-4ecc-ade7-c3477cede2fd
executorType : powershell
start        : 2025-09-16T15:57:27.3383981Z
end          : 2025-09-16T15:57:50.8771723Z
status       : Success
attempts     : {}
result       : @{command=Get-Mailbox; output=[{"DisplayName":"Test
               Powershell","Identity":"testpowershell@example.com"}]}



PS C:\WINDOWS\system32>
