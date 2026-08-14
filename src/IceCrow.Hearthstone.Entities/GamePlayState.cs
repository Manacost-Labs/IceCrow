namespace IceCrow.Hearthstone.Entities;

// Values verified against HearthDb Enums.cs at revision
// 37981c80d9b8c164db8cdb5cfa18c708c32d111e on 2026-08-13.
public enum GamePlayState
{
    Invalid = 0,
    Playing = 1,
    Winning = 2,
    Losing = 3,
    Won = 4,
    Lost = 5,
    Tied = 6,
    Disconnected = 7,
    Conceded = 8,
}
