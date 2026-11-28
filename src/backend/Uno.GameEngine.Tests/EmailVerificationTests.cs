using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using Uno.Api.Authentication;
using Uno.Api.Controllers;

public sealed class EmailVerificationTests
{
    private sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        public string Token = "";
        public int Calls;
        public bool Fail;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Token = json.RootElement.GetProperty("textContent").GetString()!.Split("#verify=")[1];
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created);
        }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Fact]
    public async Task VerificationLifecycle()
    {
        var cs = Environment.GetEnvironmentVariable("UNO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(cs))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true") throw new InvalidOperationException("Missing test database");
            return;
        }
        await using var source = NpgsqlDataSource.Create(cs);
        await using var db = await source.OpenConnectionAsync();
        using var provider = new Provider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Email:BrevoApiKey"]="test", ["Email:Sender"]="sender@example.test", ["Email:FrontendUrl"]="http://localhost:5173"
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddSingleton(source)
            .AddSingleton<IConfiguration>(config).AddSingleton<IWebHostEnvironment>(new EnvironmentStub())
            .AddSingleton<IHttpClientFactory>(provider).AddSingleton(new JwtSettings(new string('x',32))).BuildServiceProvider();
        var controller = new AuthController(services, new PasswordHasher<AccountIdentity>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var username = "u" + Guid.NewGuid().ToString("N")[..16];
        var email = username + "@example.test";
        var password = "test-password";
        Assert.Equal(202, Assert.IsAssignableFrom<ObjectResult>(await controller.Register(new(email,username,"Test",password),default)).StatusCode);
        Assert.Equal(1,provider.Calls);
        var first = provider.Token;
        Assert.Equal(JwtSettings.Hash(first),await db.ExecuteScalarAsync<string>("select t.token_hash from uno.email_verification_tokens t join uno.users u on u.id=t.user_id where u.email=@email",new {email}));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Login(new(username,"wrong-password"),default));
        Assert.Equal(1,provider.Calls);
        Assert.Equal(403,Assert.IsAssignableFrom<ObjectResult>(await controller.Login(new(username,password),default)).StatusCode);
        Assert.Equal(1,provider.Calls);
        await db.ExecuteAsync("update uno.email_verification_tokens set expires_at=now()-interval '1 minute',created_at=now()-interval '31 minutes' where token_hash=@hash",new {hash=JwtSettings.Hash(first)});
        Assert.IsType<BadRequestObjectResult>(await controller.VerifyEmail(new(first),default));
        provider.Fail=true;
        Assert.Equal(403,Assert.IsAssignableFrom<ObjectResult>(await controller.Login(new(username,password),default)).StatusCode);
        Assert.Equal(2,provider.Calls);
        provider.Fail=false;
        Assert.Equal(403,Assert.IsAssignableFrom<ObjectResult>(await controller.Login(new(username,password),default)).StatusCode);
        Assert.Equal(3,provider.Calls);
        var current=provider.Token;
        Assert.NotEqual(first,current);
        Assert.IsType<BadRequestObjectResult>(await controller.VerifyEmail(new(first),default));
        Assert.IsType<OkObjectResult>(await controller.VerifyEmail(new(current),default));
        Assert.IsType<BadRequestObjectResult>(await controller.VerifyEmail(new(current),default));
        Assert.True(await db.ExecuteScalarAsync<bool>("select email_verified_at is not null from uno.users where email=@email",new {email}));
        await controller.ResendVerification(new(email),default);
        await controller.ResendVerification(new("missing@example.test"),default);
        Assert.Equal(3,provider.Calls);
    }

    [Fact]
    public void ProductionConfigurationRejectsMissingAndUnsafeSettings()
    {
        var values = new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"]="Host=localhost",
            ["Jwt:SigningKey"]=new string('x',32),
            ["Security:CheckpointKey"]=Convert.ToBase64String(new byte[32]),
            ["Email:BrevoApiKey"]="test",
            ["Email:Sender"]="sender@example.test",
            ["Email:FrontendUrl"]="https://uno.example.test",
            ["Cors:AllowedOrigins:0"]="https://uno.example.test"
        };
        IConfiguration Build() => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ProductionConfiguration.Validate(Build());
        foreach(var key in values.Keys.ToArray())
        {
            var original=values[key];
            values[key]=null;
            Assert.Throws<InvalidOperationException>(()=>ProductionConfiguration.Validate(Build()));
            values[key]=original;
        }
        values["Email:FrontendUrl"]="http://localhost:5173";
        Assert.Throws<InvalidOperationException>(()=>ProductionConfiguration.Validate(Build()));
    }
}
