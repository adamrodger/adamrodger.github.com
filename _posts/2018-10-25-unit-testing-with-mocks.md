---
title:  "Unit Testing with Mocks"
header:
  overlay_image: /assets/images/feature/unit-testing-with-mocks-1280.jpg
  overlay_filter: 0.5
excerpt: "Using mocks effectively to create unit tests that actually test something"
tags: .net dotnet testing mock moq
---
When unit testing classes that have followed [SOLID] principles, we often find classes take on the responsibility of a
co-ordinator rather than implementing any specific logic themselves. When testing these classes it makes perfect sense
to use mocks (e.g. the [Moq] library) to control the dependencies being co-ordinated. However, I sometimes see
instances where people have misunderstood how these mocks work and why the test ends up not testing what it appears to.
This is because the tests use wildcards which means they are not really testing that the underlying code creates the
required process; they are testing that the mocks themselves create that process.

Here's some example code:

```cs
public class FooService
{
    private readonly IFooRepository repository = ...;
    private readonly IFilter filter = ...;
    private readonly ISorter sorter = ...;

    public ICollection<Foo> GetFoos(FilterParameters filterParams, SortOrder sortOrder)
    {
        ICollection<Foo> foos = this.repository.GetFoos();
        ICollection<Foo> filtered = this.filter.Filter(foos, filterParams);
        ICollection<Foo> sorted = this.sorter.Sort(filtered, sortOrder);
        return sorted;
    }
}
```

That code simply gets some objects from somewhere then passes them off to another class that applies some filtering
logic, then another class that applies some sorting logic (let's assume the logic is complicated enough that we don't
want to do it just in here). It's just a co-ordinator class, it doesn't really do any logic of its own.

## These Are Not the Tests You're Looking For

Imagine the test:

```cs
private readonly FooService service;

private readonly Mock<IFooRepository> mockRepository;
private readonly Mock<IFilter> mockFilter;
private readonly Mock<ISorter> mockSorter;

private readonly AutoFixture.IFixture fixture;

public FooServiceTests()
{
    this.mockRepository = new Mock<IFooRepository>();
    this.mockFilter = new Mock<IFilter>();
    this.mockSorter = new Mock<ISorter>();
    
    this.fixture = new Fixture();
    
    this.service = new FooService(this.mockRepository.Object, this.mockFilter.Object, this.mockSorter.Object);
}

[Fact]
public void GetFoos_WhenCalled_ReturnsFilteredAndSortedResponse()
{
    var expected = this.fixture.CreateMany<Foo>().ToList();
    this.mockRepository.Setup(r => r.GetFoos())
                       .Returns(this.fixture.CreateMany<Foo>().ToList());
    this.mockFilter.Setup(r => r.Filter(It.IsAny<ICollection<Foo>>(), It.IsAny<FilterParameters>()))
                   .Returns(this.fixture.CreateMany<Foo>().ToList());
    this.mockSorter.Setup(r => r.Sort(It.IsAny<ICollection<Foo>>(), It.IsAny<SortOrder>()))
                   .Returns(expected);
                   
    var actual = this.service.GetFoos(FilterParameters.Empty, SortOrder.Descending);
    
    actual.Should().Equal(expected);
}
```

On the face of it, that test looks like it makes sure we're returning the output from the repository after it's been
filtered and sorted, but it doesn't. The only thing that tests is that the overall method returns whatever the final
setup (on `mockSorter`) happens to return. Because it uses `It.IsAny<>` all the way through, it never actually verifies
that the output of the repository is passed to the filter with the appropriate filter parameters. It never verifies
that the output of the filter is passed to the sorter with the appropriate sort order. All it tests is that whatever
the sorter returns, that's what the method returns. This means it doesn't test the process. Imagine the following
implementation (which is broken, don't do this):

```cs
public ICollection<Foo> GetFoos(FilterParameters filterParams, SortOrder sortOrder)
{
    this.repository.GetFoos(); // just throws it away
    this.filter.Filter(null, null); // passes some nulls then throws away the result, because why not?
    ICollection<Foo> sorted = this.sorter.Sort(null, null); // some more nulls, but keep the result
    return sorted;
}
```

That code doesn't work at all, but the test will continue to pass because of the `It.IsAny<>` in the mock setups.

## Fixed That For You

So, let's alter the test to make it work:

```cs
[Fact]
public void GetFoos_WhenCalled_ReturnsFilteredAndSortedResponse()
{
    var foos = this.fixture.CreateMany<Foo>().ToList();
    var filtered = this.fixture.CreateMany<Foo>().ToList();
    var expected = this.fixture.CreateMany<Foo>().ToList();
    var filterParameters = this.fixture.Create<FilterParameters>();
    var sortOrder = SortOrder.Descending;
    this.mockRepository.Setup(r => r.GetFoos()).Returns(foos);
    this.mockFilter.Setup(r => r.Filter(foos, filterParameters)).Returns(filtered);
    this.mockSorter.Setup(r => r.Sort(filtered, sortOrder)).Returns(expected);
                   
    var actual = this.service.GetFoos(filterParameters, sortOrder);
    
    actual.Should().Equal(expected);
}
```

Now the test fails because of the broken implementation, and only starts passing when we revert back to the correct
implementation. This test verifies that each step of the process is working with the output of the previous step, plus
using the arguments provided to the method for filtering/sorting. This is the way that mocks should be used when
testing the 'happy path'.

## Always An Exception

That's not to say that `It.IsAny<>` or other similar wildcarding has no place in tests, just that you need to be careful
where to use it. Some valid use-cases are:

- When testing the error paths through code you may want the code to throw an exception on all possible inputs rather
  than just a few known ones, otherwise you'd be replicating logic from dependencies within your mocks. This is clearly
  a bad idea because that logic may change and your mocks may no longer be representative. For example:
  ```cs
  public void Create_InvalidInput_ThrowsValidationException()
  {
      // we don't care whether the input is technically valid or not, just simulate what happens if it isn't
      mockValidator.Setup(v => v.Validate(It.IsAny<Foo>())).Throws<ValidationException>();

      Action action = () => this.service.Create(new Foo());

      action.Should().Throw<ValidationException>();
  }
  ```
- if the argument to the mock is generated at runtime within the system under test (e.g. an object created with `new` or
  a callback function), so you can't possibly create a setup for it:
  ```cs
  // in SUT code... clear the cache whenever a message is received
  notifier.Subscribe("some-pubsub-stream", () => this.cache.Invalidate());

  // in a test... you want to simulate the callback happening
  Action callback = null;
  mockNotifier.Setup(n => n.Subscribe("some-pubsub-stream", It.IsAny<Action>()))
              .Callback((Action captured) => callback = captured); // capture the callback

  // simulate a message being received
  callback();
  
  // make sure the callback had the appropriate effect
  mockCache.Verify(c => c.Invalidate(), Times.Once);
  ```
- Creating happy path defaults that you'll override in certain instances:
  ```cs
  public Tests()
  {
      this.mockValidator = new Mock<IValidator>();

      // default to passing validation
      this.mockValidator.Setup(v => v.Validate(It.IsAny<Foo>()).Returns(true);
  }

  [Fact]
  public void Create_SuccessfullyCreated_ReturnsSuccessfulResponse()
  {
      // no setup required on validator, we'll just assume the input is valid
      var expected = new SuccessResponse();

      var response = sut.Create(new Foo());

      response.Should().Equal(expected);
  }

  [Fact]
  public void Create_InvalidInput_ThrowsValidationException()
  {
      // override the default setup to simulate what happens with invalid input
      this.mockValidator.Setup(v => v.Validate(It.IsAny<Foo>()).Throws<ValidationException>();

      // ... test the exception is thrown as before
  }
  ```

It's also worth noting that that's not the only test you need, even for the happy path. For example, the test would
still pass if the implementation was hard-coded to always pass `SortOrder.Descending` so there should be another test
that passes `SortOrder.Ascending`. It's better to use a parameterised test for this exact instance:

```cs
[Theory]
[InlineData(SortOrder.Ascending)]
[InlineData(SortOrder.Descending)]
public void GetFoos_WhenCalled_ReturnsFilteredAndSortedResponse(SortOrder sortOrder)
{
    // ... 
    this.mockSorter.Setup(r => r.Sort(filtered, sortOrder)).Returns(expected);
                   
    var actual = this.service.GetFoos(filterParameters, sortOrder);
    
    actual.Should().Equal(expected);
}
```

You also need tests to verify what happens if those dependencies throw exceptions (if they can throw exceptions) or
return nulls (if they can return nulls). You *don't* need tests for the actual filtering/sorting logic here. Those
will be covered in the tests for the filter/sorter classes (which is one good reason to move them to their own classes
in the first place, following the Single Responsibility Principle). This allows us to use [AutoFixture] to generate the
outputs of the repository/filter/sorter because the service class never needs to rely on those outputs having a
sensible value; it just needs to be concerned with co-ordinating the process with the dependencies properly.

*Banner Photo by Rodolfo Clix used with permission from [Pexels]*

[AutoFixture]: https://github.com/AutoFixture/AutoFixture
[Moq]: https://github.com/Moq/moq4
[Pexels]: https://www.pexels.com/photo/photo-of-clear-glass-measuring-cup-lot-1366942/
[SOLID]: https://en.wikipedia.org/wiki/SOLID
