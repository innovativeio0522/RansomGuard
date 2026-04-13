# RansomGuard.Tests

Unit tests for the RansomGuard application.

## Test Framework

- **xUnit** - Test framework
- **Moq** - Mocking framework
- **FluentAssertions** - Assertion library
- **Coverlet** - Code coverage

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~ConfigurationServiceTests"
```

### Run Tests in Watch Mode
```bash
dotnet watch test
```

## Test Structure

```
RansomGuard.Tests/
├── Core/
│   ├── ConfigurationServiceTests.cs
│   ├── PathConfigurationTests.cs
│   └── NativeMemoryTests.cs
├── Models/
│   ├── ThreatTests.cs
│   ├── FileActivityTests.cs
│   └── ProcessInfoTests.cs
├── IPC/
│   └── IpcModelsTests.cs
└── README.md
```

## Test Coverage

Current test coverage:
- **ConfigurationService**: 100%
- **PathConfiguration**: 100%
- **NativeMemory**: 100%
- **Models**: 100%
- **IPC Models**: 100%

## Writing Tests

### Test Naming Convention
```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    // Act
    // Assert
}
```

### Example Test
```csharp
[Fact]
public void Instance_ShouldReturnSingleton()
{
    // Arrange & Act
    var instance1 = ConfigurationService.Instance;
    var instance2 = ConfigurationService.Instance;

    // Assert
    instance1.Should().BeSameAs(instance2);
}
```

## Test Categories

### Unit Tests
- Test individual components in isolation
- Fast execution
- No external dependencies

### Integration Tests (Future)
- Test component interactions
- May require setup/teardown
- Test with real dependencies

## CI/CD Integration

Tests are automatically run on:
- Pull requests
- Commits to main branch
- Release builds

## Code Coverage Goals

- **Minimum**: 80% coverage
- **Target**: 90% coverage
- **Critical Components**: 100% coverage

## Future Test Additions

### Planned Test Suites
- [ ] ViewModels tests (with mocking)
- [ ] Service integration tests
- [ ] UI automation tests
- [ ] Performance tests
- [ ] Load tests

### Test Improvements
- [ ] Add parameterized tests
- [ ] Add property-based tests
- [ ] Add mutation testing
- [ ] Add benchmark tests

## Contributing

When adding new features:
1. Write tests first (TDD)
2. Ensure all tests pass
3. Maintain >80% coverage
4. Follow naming conventions
5. Use FluentAssertions

## Resources

- [xUnit Documentation](https://xunit.net/)
- [Moq Documentation](https://github.com/moq/moq4)
- [FluentAssertions Documentation](https://fluentassertions.com/)
