**How to run locally**

Open terminal in root folder.
Go to Src: cd Src
Restore: dotnet restore
Build: dotnet build
Run: dotnet run
App runs on http://localhost:7281 or whatever port it says.

Test ping: curl http://localhost:7281/ping (should say pong)

Test HTTP: curl http://localhost:7281/api/https://jsonplaceholder.typicode.com/todos/1

Test PS: curl -X POST http://localhost:7281/api/powershell/Get-Mailbox -H "X-Auth-User: user@ex.com" -H "X-Auth-Pass: pass" -d '{"Identity":"john"}'

For metrics: curl http://localhost:7281/metrics


**Testing**

Tests in Tests folder. Run: dotnet test

Units for validation, retry, response. Integration for HTTP with mock server.

Test matrix:

Bad path HTTP 400 No 0 Error message Bad PS cmd PS 400 No 0 Invalid command HTTP good HTTP 200 No 0 Success status HTTP 500 retry HTTP 200 Yes 1 Attempts 2 PS auth bad PS 500 No 0 Fail no retry

Examples

For http examples/http_request.json: { "note": "For GET https://jsonplaceholder.typicode.com/todos/1" }


examples/http_response.json: { "RequestId": "abc123", "ExecutorType": "http", "Start": "2025-09-16T00:00:00Z", "End": "2025-09-16T00:00:01Z", "Status": "Success", "Attempts": [{"Attempt":1,"Outcome":"Success","DurationMs":100}], "Result": {"Status":200,"Body":"{"title":"test"}"} }

