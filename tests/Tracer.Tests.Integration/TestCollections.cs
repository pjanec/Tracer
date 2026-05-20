using Xunit;

namespace Tracer.Tests.Integration;

[CollectionDefinition("OfflineViewerSmoke", DisableParallelization = true)]
public sealed class OfflineViewerSmokeCollection { }

[CollectionDefinition("BundleRoundTrip")]
public sealed class BundleRoundTripCollection { }

[CollectionDefinition("Distribution")]
public sealed class DistributionCollection { }

[CollectionDefinition("TimelineRoundTrip", DisableParallelization = true)]
public sealed class TimelineRoundTripCollection { }
