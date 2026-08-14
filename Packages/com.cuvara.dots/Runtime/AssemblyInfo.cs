using System.Runtime.CompilerServices;

// The systems are internal on purpose: the package's ordering contract is its groups, and a public
// system name is an accidental API promise. The test assemblies still have to construct and tick
// them directly, which is what these grants are for — and only these two assemblies get them.
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Runtime")]
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Editor")]
