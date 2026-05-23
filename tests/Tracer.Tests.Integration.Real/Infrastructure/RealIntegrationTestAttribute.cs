namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>Marks a test as belonging to the real-integration test lane.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RealIntegrationTestAttribute : Attribute { }
