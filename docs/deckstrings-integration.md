# Deckstrings integration

IceCrow pins `ManacostLabs.Deckstrings` 1.0.0 from NuGet. The package is the
only owner of the Hearthstone deckstring wire format.

`IceCrow.Hearthstone.Decks` exposes IceCrow-owned immutable models and
`IDeckCodec`. `ManacostDeckCodec` translates those models at the adapter edge
and delegates decode, encode, validation, canonicalization, sideboards,
clipboard parsing, and clipboard formatting to the package. Package types do
not escape the assembly.

Deck encoding is entirely offline. Export formatting may synchronously ask an
already-loaded `ICardDatabase` for a display name and cost; an unknown DBF ID
simply omits that presentation line and never triggers a network request.

Repository inspected: `Manacost-Labs/hearthstone-deckstrings` revision
`02121cffde7db888a9f8daff064749cfe0a3c63a`. The package license is ISC and the
shared fixtures define its cross-language behavior.

The checked-in non-threshold diagnostic measured 10,000 offline decode+encode
pairs at 267.8 ms on the development Windows machine on 2026-08-14. This is a
baseline observation, not a CI timing assertion.
