using Cardflow.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCardflowServices(builder.Configuration);

var app = builder.Build();
await app.UseCardflowPipelineAsync();

app.Run();
