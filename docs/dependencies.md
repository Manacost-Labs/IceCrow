# Dependencies and provenance

IceCrow prefers the .NET BCL. Central versions are declared in
`Directory.Packages.props`; project files do not pin independent versions.

| Package / platform | Version source | Scope | Reason |
| --- | --- | --- | --- |
| .NET SDK | `global.json` | all projects | compiler/runtime and WPF toolchain |
| `ManacostLabs.Deckstrings` | central package version 1.0.0 | `Hearthstone.Decks` only | canonical deckstring codec behind IceCrow models |
| `Microsoft.NET.Test.Sdk` | central version 17.14.1 | tests only | test host |
| `xunit` | central version 2.9.3 | tests only | deterministic unit tests |
| `xunit.runner.visualstudio` | central version 3.1.4 | tests only | local/CI discovery and TRX output |
| Win32 / WPF | Windows/.NET platform | platform, overlay, app | native overlay and application shell |

Before adding a package, document the exact owning project, required capability,
license/provenance, update policy, transitive dependencies, and why the BCL or an
existing adapter is insufficient. A package must not leak its types across the
IceCrow-owned contract unless it is deliberately part of that contract.

The local HDT checkout is research material, not a dependency. It is never
linked, packaged, or copied into IceCrow. HearthMirror is not bundled while its
redistribution/license boundary remains unresolved.
