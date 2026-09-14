using System.Runtime.CompilerServices;

// PlayerBuilder.Build is the -executeMethod the self-hosted lanes call by name,
// and until now nothing in CI had ever executed a line of it: the Docker lane
// uses game-ci's own builder and never reaches this file. What it reads from the
// command line and the environment is a contract with the toolkit, and a
// contract nobody checks is a guess.
//
// The parts that decide artifact names, scene order and argument parsing are
// internal rather than private so that contract can be asserted from
// NDC.Tests.Editor. Nothing about the build itself is opened up.
[assembly: InternalsVisibleTo("NDC.Tests.Editor")]
