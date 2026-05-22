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

[CollectionDefinition("AnnotationsRoundTrip", DisableParallelization = true)]
public sealed class AnnotationsRoundTripCollection { }

[CollectionDefinition("SavedViewsRoundTrip", DisableParallelization = true)]
public sealed class SavedViewsRoundTripCollection { }

[CollectionDefinition("TriggerEvalIntegration")]
public sealed class TriggerEvalIntegrationCollection { }

[CollectionDefinition("LatencyAnalysisIntegration")]
public sealed class LatencyAnalysisIntegrationCollection { }

[CollectionDefinition("GapDetectionIntegration")]
public sealed class GapDetectionIntegrationCollection { }

[CollectionDefinition("TopologyIntegration")]
public sealed class TopologyIntegrationCollection { }

[CollectionDefinition("SavedQueriesRoundTrip", DisableParallelization = true)]
public sealed class SavedQueriesRoundTripCollection { }

[CollectionDefinition("BundleLibraryRoundTrip", DisableParallelization = true)]
public sealed class BundleLibraryRoundTripCollection { }
