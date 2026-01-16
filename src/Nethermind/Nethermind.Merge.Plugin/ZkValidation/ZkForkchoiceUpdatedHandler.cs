// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;

namespace Nethermind.Merge.Plugin.ZkValidation;

/// <summary>
/// Simplified forkchoice update handler for ZK validation mode.
/// Trusts ZK validation done in newPayload.
/// </summary>
public class ZkForkchoiceUpdatedHandler(
    IBlockCacheService blockCacheService,
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
        Hash256 headBlockHash = forkchoiceState.HeadBlockHash;
        Hash256 safeBlockHash = forkchoiceState.SafeBlockHash;
        Hash256 finalizedBlockHash = forkchoiceState.FinalizedBlockHash;

        if (_logger.IsInfo) _logger.Info($"[ZK] Received ForkChoice: {headBlockHash}, Safe: {safeBlockHash}, Finalized: {finalizedBlockHash}");

        // Check if head is on a known invalid chain
        if (invalidChainTracker.IsOnKnownInvalidChain(headBlockHash, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn) _logger.Warn($"[ZK] Head {headBlockHash} is on an invalid chain. Last valid: {lastValidHash}");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Invalid(lastValidHash));
        }

        blockCacheService.BlockCache.TryGetValue(headBlockHash, out Block? block);
        if (block is null)
        {
            if (_logger.IsInfo) _logger.Info($"[ZK] Block {headBlockHash} not found in cache. Returning Syncing.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Syncing);
        }

        if (_logger.IsInfo) _logger.Info($"[ZK] Block {block.Number} processed (cached). Returning Valid without chain update.");

        blockCacheService.HeadBlockHash = headBlockHash;
        blockCacheService.FinalizedHash = finalizedBlockHash;
        return Task.FromResult(ForkchoiceUpdatedV1Result.Valid(null, headBlockHash));
    }
}
