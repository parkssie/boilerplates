RESTAPI_Inf_Worker

Purpose:
- Periodically (1 minute) GET a REST API endpoint with Bearer token and store the response into PostgreSQL (jsonb).

Quick start:
1. Edit appsettings.json: RestApi.Url, RestApi.Token, Postgres.* (SearchPath will be applied to connectionstring).
2. dotnet restore
3. dotnet build
4. dotnet publish -c Release -o publish

Run locally:
- From publish folder: RESTAPI_Inf_Worker.exe

Install as Windows Service:
- Copy publish folder to target machine, then:
  sc create RESTAPI_Inf_Worker binPath= "C:\path\to\RESTAPI_Inf_Worker.exe" start= auto
  sc start RESTAPI_Inf_Worker

Notes:
- NLog writes logs to logs/worker.log with rolling archives at 10MB.
- The code will create the api_responses table if it doesn't exist.
