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

        if (invalidChainTracker.IsOnKnownInvalidChain(headBlockHash, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn) _logger.Warn($"Received Invalid {forkchoiceState} {payloadAttributes} - {headBlockHash} is known to be a part of an invalid chain.");
            return ForkchoiceUpdatedV1Result.Invalid(lastValidHash);
        }

        string requestStr = forkchoiceState.ToString();
        if (_logger.IsInfo) _logger.Info($"Received {requestStr}");

        blockCacheService.BlockCache.TryRemove(headBlockHash, out Block? block);
        if (block is null)
        {
            if (_logger.IsInfo) _logger.Info($"Syncing Unknown ForkChoiceState head hash Request: {requestStr}.");
            return ForkchoiceUpdatedV1Result.Syncing;
        }

        
        if (_logger.IsInfo) _logger.Info($"Valid. {block}");
        return ForkchoiceUpdatedV1Result.Valid(null, headBlockHash);
    }
}
