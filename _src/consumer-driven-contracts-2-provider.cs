public class ProviderTests
{
    [Fact]
    public void ProviderSatisfiesPactWithConsumer()
    {
        // start your API
        IWebHost host = new WebHostBuilder().UseStartup<Startup>() // from your regular ASP.Net Core Startup.cs file
                                            .UseKestrel()
                                            .UseUrls("http://localhost:5000")
                                            .Build();
        host.Start();
        
        // run the interactions from the pact file and verify the results
        verifier.ServiceProvider("Provider", "http://localhost:5000")
                .HonoursPactWith("Consumer")
                .PactUri("path/to/consumer-provider.json")
                .Verify();
        }
    }
}