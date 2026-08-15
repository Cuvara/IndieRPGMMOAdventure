using System.Runtime.CompilerServices;

// The systems are internal on purpose: the package's ordering contract is its groups, and a public
// system name is an accidental API promise. The test assemblies still have to construct and tick
// them directly, which is what these grants are for — and only test assemblies get them.
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Runtime")]
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Editor")]

// The netcode adapter's and prediction driver's tests need ViewConfig.Configure and
// ViewArchetypeLibrary.Configure to build a catalog without authoring assets — the same reason the
// other two are here. Neither names a package system: they drive the public groups, so the ordering
// contract is what is under test.
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Netcode")]
[assembly: InternalsVisibleTo("Cuvara.DOTS.Tests.Prediction")]
