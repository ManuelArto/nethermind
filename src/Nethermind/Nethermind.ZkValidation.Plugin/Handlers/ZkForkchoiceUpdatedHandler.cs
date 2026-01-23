// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;

namespace Nethermind.ZkValidation.Plugin.Handlers;

/// <summary>
/// Simplified forkchoice update handler for ZK validation mode.
/// Trusts ZK validation done in newPayload.
/// </summary>
public class ZkForkchoiceUpdatedHandler(
    IBlockTree blockTree,
    IManualBlockFinalizationManager manualBlockFinalizationManager,
    IInvalidChainTracker invalidChainTracker,
    ILogManager logManager)
    : IForkchoiceUpdatedHandler
{
    private readonly ILogger _logger = logManager.GetClassLogger();

    public Task<ResultWrapper<ForkchoiceUpdatedV1Result>> Handle(
        ForkchoiceStateV1 forkchoiceState,
        PayloadAttributes? payloadAttributes,
        int version)
    {
        // Attester-Only: Explicitly reject block production
        if (payloadAttributes is not null)
            return ResultWrapper<ForkchoiceUpdatedV1Result>.Fail("Block production is disabled for ZK Stateless mode.");

        Hash256 headBlockHash = forkchoiceState.HeadBlockHash;

        // Is on invalid chain?
        if (invalidChainTracker.IsOnKnownInvalidChain(headBlockHash, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn)
                _logger.Warn($"Received Invalid {forkchoiceState} {payloadAttributes} - {headBlockHash} is known to be a part of an invalid chain.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Invalid(lastValidHash));
        }

        // NewPayload was call and block is valid?
        Block? newHeadBlock = blockTree.FindBlock(headBlockHash, BlockTreeLookupOptions.DoNotCreateLevelIfMissing);
        BlockInfo? blockInfo = newHeadBlock != null ? blockTree.GetInfo(newHeadBlock.Number, headBlockHash).Info : null;
        if (newHeadBlock is null || blockInfo is null || !blockInfo.WasProcessed)
        {
            if (_logger.IsInfo) _logger.Info($"Syncing Unknown ForkChoiceState head hash Request: {forkchoiceState}.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Syncing);
        }

        string requestStr = forkchoiceState.ToString(newHeadBlock.Number);
        if (_logger.IsInfo) _logger.Info($"Received {requestStr}");

        if (newHeadBlock.Header.TotalDifficulty == null || newHeadBlock.Header.TotalDifficulty == UInt256.Zero)
            newHeadBlock.Header.TotalDifficulty = UInt256.Parse("58750000000000000000000");

        // First FCU call?
        if ((blockTree.Head?.Number ?? 0) == 0)
        {
            if (_logger.IsInfo) _logger.Info($"Stateless Pivot: Anchoring Head at {newHeadBlock.Number}");
            blockTree.UpdateHeadBlock(headBlockHash);
        }
        // Need to update Chain Head?
        else if (blockTree.Head?.Hash != headBlockHash)
        {
            if (_logger.IsInfo) _logger.Info($"Synced Chain Head to {newHeadBlock.ToString(Block.Format.Short)}");
            blockTree.UpdateMainChain(new[] { newHeadBlock }, wereProcessed: true, forceHeadBlock: true);
        }

        // Finalized block reached?
        if (forkchoiceState.FinalizedBlockHash != Hash256.Zero)
        {
            BlockHeader? finalizedHeader = blockTree.FindHeader(forkchoiceState.FinalizedBlockHash);
            if (finalizedHeader is not null)
                manualBlockFinalizationManager.MarkFinalized(newHeadBlock.Header, finalizedHeader);
        }

        blockTree.ForkChoiceUpdated(forkchoiceState.FinalizedBlockHash, forkchoiceState.SafeBlockHash);
        return Task.FromResult(ForkchoiceUpdatedV1Result.Valid(null, headBlockHash));
    }
}
