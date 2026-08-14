using System.Runtime.CompilerServices;

// Same rule as the other assemblies: the driving system is internal because the package's ordering
// contract is PredictionSystemGroup, not a system name.
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Prediction")]
