using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

[CollectionDefinition("RealIntegration", DisableParallelization = true)]
public sealed class RealIntegrationCollection : ICollectionFixture<SimulationHarnessFixture> { }
