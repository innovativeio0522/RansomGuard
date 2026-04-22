using Xunit;

// Disable parallel execution for IPC tests to avoid "Pipe in use" conflicts between tests
[assembly: CollectionBehavior(DisableTestParallelization = true)]
