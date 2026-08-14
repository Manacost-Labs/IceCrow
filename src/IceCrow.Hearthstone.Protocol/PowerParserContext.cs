using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Protocol;

public sealed class PowerParserContext
{
    public const int MaximumBlockDepth = 128;
    private const int MaximumMalformedPatterns = 64;

    private readonly Stack<PowerBlock> _blocks = [];
    private readonly HashSet<string> _reportedMalformedPatterns = new(StringComparer.Ordinal);
    private long _nextBlockId;
    private long _rejectedBlockDepth;

    public int? CurrentEntityId { get; internal set; }

    public int? CurrentCreationEntityId { get; internal set; }

    public PowerBlock? CurrentBlock
    {
        get
        {
            lock (this)
            {
                return _blocks.TryPeek(out var block) ? block : null;
            }
        }
    }

    public IReadOnlyList<PowerBlock> BlockStack
    {
        get
        {
            lock (this)
            {
                return _blocks.Reverse().ToArray();
            }
        }
    }

    public long Parsed { get; private set; }

    public long Ignored { get; private set; }

    public long Malformed { get; private set; }

    public long Unknown { get; private set; }

    internal long? CurrentBlockId => CurrentBlock?.Id;

    internal bool CanPushBlock => _blocks.Count < MaximumBlockDepth;

    internal bool IsRecoveringBlock => _rejectedBlockDepth > 0;

    internal void EnterBlockRecovery()
    {
        if (_rejectedBlockDepth < long.MaxValue)
        {
            _rejectedBlockDepth++;
        }
    }

    internal bool TryConsumeRejectedBlockEnd()
    {
        if (_rejectedBlockDepth == 0)
        {
            return false;
        }

        _rejectedBlockDepth--;
        return true;
    }

    internal void ResetEntityContext()
    {
        CurrentEntityId = null;
        CurrentCreationEntityId = null;
    }

    internal void ResetForNewGame()
    {
        _blocks.Clear();
        _rejectedBlockDepth = 0;
        ResetEntityContext();
    }

    internal PowerBlock PushBlock(
        string type,
        int? entityId,
        string? entityName,
        string effectCardId,
        string target,
        int? subOption,
        string? triggerKeyword)
    {
        var block = new PowerBlock(
            Id: _nextBlockId++,
            ParentId: CurrentBlockId,
            Depth: _blocks.Count,
            Type: type,
            EntityId: entityId,
            EntityName: entityName,
            EffectCardId: effectCardId,
            Target: target,
            SubOption: subOption,
            TriggerKeyword: triggerKeyword);
        _blocks.Push(block);
        return block;
    }

    internal bool TryPopBlock(out PowerBlock block) => _blocks.TryPop(out block!);

    internal PowerParseResult RecordParsed(GameEvent gameEvent)
    {
        Parsed++;
        return PowerParseResult.Parsed(gameEvent);
    }

    internal PowerParseResult RecordIgnored()
    {
        Ignored++;
        return PowerParseResult.Ignored();
    }

    internal PowerParseResult RecordUnknown(GameEvent gameEvent)
    {
        Unknown++;
        return PowerParseResult.Unknown(gameEvent);
    }

    internal PowerParseResult RecordMalformed(string pattern, string diagnostic)
    {
        Malformed++;
        var shouldReport = _reportedMalformedPatterns.Count < MaximumMalformedPatterns &&
                           _reportedMalformedPatterns.Add(pattern);
        return PowerParseResult.Malformed(shouldReport ? diagnostic : null);
    }
}
