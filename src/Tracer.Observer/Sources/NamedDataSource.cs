using Tracer.Core.Abstractions;

namespace Tracer.Observer.Sources;

public sealed record NamedDataSource(string Name, IDiagnosticDataSource Source);
