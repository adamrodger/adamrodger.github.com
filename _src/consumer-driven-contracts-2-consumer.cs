public class ConsumerClient()
{
    public ConsumerClient(string baseUrl) { ...create a HttpClient... }
    public Task<Product> GetProductAsync(int productId) { ...call the API using HttpClient... };
}

public class ConsumerTests : IDisposable
{
    private readonly IPactBuilder builder;
    private readonly IMockProviderService provider;

    public ConsumerTests()
    {
        var builder = new PactBuilder(new PactConfig { SpecificationVersion = "2.0.0" });
        builder.ServiceConsumer("Consumer").HasPactWith("Provider");
               
        // start the mock provider on port 8080
        var provider = builder.ProviderService(8080);
    }

    public void Dispose()
    {
        // write the pact file to disk
        builder.Build();
    }

    [Fact]
    public async Task GetProducts_SuccessfulRequest_ReturnsProducts()
    {
        // arrange the interaction
        provider.Given("some products exist")
                .UponReceiving("a request for product by id")
                .With(new ProviderServiceRequest
                {
                    Method = HttpVerb.Get,
                    Path = "/api/products/123",
                    Headers = new Dictionary<string, object>
                    {
                        { "Accept", "application/json" }
                    }
                })
                .WillRespondWith(new ProviderServiceResponse
                {
                    Status = 200,
                    Headers = new Dictionary<string, object>
                    {
                        { "Content-Type", "application/json; charset=utf-8" }
                    },
                    Body = new
                    {
                        id = 123,
                        name = "Peanut Butter",
                        price = 1.23
                    }
                });
                
        // act to ensure your defined class calls the provider properly
        var client = new ConsumerClient("http://localhost:8080");
        await client.GetProductAsync(123);
        
        // assert that the API was called properly
        provider.VerifyInteractions();
    }
}