# Battlegrounds regression corpus

Each immediate child directory is one self-contained fixture with an
`expected.json` manifest. The manifest states whether the evidence is
`synthetic` or `real-anonymized`; those labels are never interchangeable.

The initial committed fixtures are synthetic infrastructure smoke tests. They
prove the normalized and raw golden runners, but they are not evidence for any
real Hearthstone edge case. Real recordings must be captured by a user,
anonymized with `IceCrow.FixtureTool`, reviewed, and only then committed.
