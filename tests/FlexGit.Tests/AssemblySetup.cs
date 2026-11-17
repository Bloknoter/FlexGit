// Every integration test uses its own GitSandbox (temp dir + isolated git config), so tests can run in parallel.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
