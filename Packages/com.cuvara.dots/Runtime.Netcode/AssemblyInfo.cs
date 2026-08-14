using System.Runtime.CompilerServices;

// Same rule as the core assembly: the drain system and the queued command are internal because the
// package's ordering contract is NetcodeSystemGroup, not a system name. The test assembly has to
// construct and tick them directly, which is what this grant is for.
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Netcode")]
