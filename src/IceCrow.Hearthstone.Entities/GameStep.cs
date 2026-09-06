namespace IceCrow.Hearthstone.Entities;

// Values verified against the HearthDb Step enum on 2026-09-06.
public enum GameStep
{
    Invalid = 0,
    BeginFirst = 1,
    BeginShuffle = 2,
    BeginDraw = 3,
    BeginMulligan = 4,
    MainBegin = 5,
    MainReady = 6,
    MainResource = 7,
    MainDraw = 8,
    MainStart = 9,
    MainAction = 10,
    MainCombat = 11,
    MainEnd = 12,
    MainNext = 13,
    FinalWrapup = 14,
    FinalGameover = 15,
    MainCleanup = 16,
    MainStartTriggers = 17,
    MainSetActionStepType = 18,
    MainPreAction = 19,
    MainPostAction = 20,
}
