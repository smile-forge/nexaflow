// The OwnedModel classes carry [DoNotParallelize]: one host and one loaded model serve a run (docs/testing.md).
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
